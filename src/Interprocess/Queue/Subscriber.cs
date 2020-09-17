using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    internal sealed class Subscriber : Queue, ISubscriber
    {
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly CountdownEvent countdownEvent = new CountdownEvent(1);
        private readonly IInterprocessSemaphoreWaiter signal;

        internal Subscriber(QueueOptions options, ILoggerFactory loggerFactory)
            : base(options, loggerFactory)
        {
            signal = InterprocessSemaphore.CreateWaiter(options.QueueName);
        }

        public override void Dispose()
        {
            // drain the Dequeue/TryDequeue requests
            cancellationSource.Cancel();
            countdownEvent.Signal();
            countdownEvent.Wait();

            // There is a potential for a  race condition in *DequeueCore if the cancellationSource.
            // was not cancelled before AddEvent is beging called. The sleep here will prevent that.
            Thread.Sleep(10);

            countdownEvent.Dispose();
            signal.Dispose();
            cancellationSource.Dispose();
            base.Dispose();
        }

        public bool TryDequeue(CancellationToken cancellation, out ReadOnlyMemory<byte> message)
            => TryDequeueCore(default, cancellation, out message);

        public bool TryDequeue(Memory<byte> resultBuffer, CancellationToken cancellation, out ReadOnlyMemory<byte> message)
            => TryDequeueCore(resultBuffer, cancellation, out message);

        public ReadOnlyMemory<byte> Dequeue(CancellationToken cancellation)
            => DequeueCore(default, cancellation);

        public ReadOnlyMemory<byte> Dequeue(Memory<byte> resultBuffer, CancellationToken cancellation)
            => DequeueCore(resultBuffer, cancellation);

        private bool TryDequeueCore(Memory<byte>? resultBuffer, CancellationToken cancellation, out ReadOnlyMemory<byte> message)
        {
            // do NOT reorder the cancellation and the AddCount operation below. See Dispose for more information.
            cancellationSource.ThrowIfCancellationRequested(cancellation);
            countdownEvent.AddCount();

            try
            {
                return TryDequeueImpl(resultBuffer, cancellation, out message);
            }
            finally
            {
                countdownEvent.Signal();
            }
        }

        private ReadOnlyMemory<byte> DequeueCore(Memory<byte>? resultBuffer, CancellationToken cancellation)
        {
            // do NOT reorder the cancellation and the AddCount operation below. See Dispose for more information.
            cancellationSource.ThrowIfCancellationRequested(cancellation);
            countdownEvent.AddCount();

            try
            {
                while (true)
                {
                    if (TryDequeueImpl(resultBuffer, cancellation, out var message))
                        return message;

                    signal.Wait(millisecondsTimeout: 100);
                }
            }
            finally
            {
                countdownEvent.Signal();
            }
        }

        private unsafe bool TryDequeueImpl(
            Memory<byte>? resultBuffer,
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message)
        {
            while (true)
            {
                cancellationSource.ThrowIfCancellationRequested(cancellation);
                var header = Header;
                var headOffset = header->HeadOffset;

                if (headOffset == header->TailOffset)
                {
                    message = ReadOnlyMemory<byte>.Empty;
                    return false; // this is an empty queue
                }

                var messageHeader = (MessageHeader*)Buffer.GetPointer(headOffset);

                // is the message still being written/created?
                if (messageHeader->State == MessageHeader.BeingCreatedState)
                    continue; // message is still being created

                // take a lock so no other thread can start processing this message
                if (Interlocked.CompareExchange(
                    ref messageHeader->State,
                    MessageHeader.LockedToBeConsumedState,
                    MessageHeader.ReadyToBeConsumedState) != MessageHeader.ReadyToBeConsumedState)
                {
                    message = ReadOnlyMemory<byte>.Empty;
                    return false; // some other receiver got to this message before us
                }

                // read the message body from the queue buffer
                var bodyLength = messageHeader->BodyLength;
                var bodyOffset = GetMessageBodyOffset(headOffset);
                message = Buffer.Read(bodyOffset, bodyLength, resultBuffer);

                // zero out the message body first
                Buffer.Clear(bodyOffset, bodyLength);

                // zero out the message header
                Buffer.Write(default(MessageHeader), headOffset);

                // updating the queue header to point the head of the queue to the next available message
                var messageLength = GetMessageLength(bodyLength);
                var newHeadOffset = SafeIncrementMessageOffset(headOffset, messageLength);
                if (Interlocked.CompareExchange(ref header->HeadOffset, newHeadOffset, headOffset) == headOffset)
                    return true;

                throw new InvalidOperationException(
                    "This is unexpected and can be a serious bug. We take a lock on this message " +
                    "prior to this point which should ensure that the HeadOffset is left unchanged");
            }
        }
    }
}
