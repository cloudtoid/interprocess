using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    internal sealed class Subscriber : Queue, ISubscriber
    {
        private readonly CancellationTokenSource cancellationSource = new();
        private readonly CountdownEvent countdownEvent = new(1);
        private readonly IInterprocessSemaphoreWaiter signal;

        internal Subscriber(QueueOptions options, ILoggerFactory loggerFactory)
            : base(options, loggerFactory)
        {
            signal = InterprocessSemaphore.CreateWaiter(options.QueueName);
        }

        protected override void Dispose(bool disposing)
        {
            // drain the Dequeue/TryDequeue requests
            cancellationSource.Cancel();
            countdownEvent.Signal();
            countdownEvent.Wait();

            // There is a potential for a race condition in *DequeueCore if the cancellationSource.
            // was not cancelled before AddEvent is called. The sleep here will prevent that.
            Thread.Sleep(millisecondsTimeout: 10);

            if (disposing)
            {
                countdownEvent.Dispose();
                signal.Dispose();
                cancellationSource.Dispose();
            }

            base.Dispose(disposing);
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
                int i = -5;
                while (true)
                {
                    if (TryDequeueImpl(resultBuffer, cancellation, out var message))
                        return message;

                    if (i > 10)
                        signal.Wait(millisecondsTimeout: 10);
                    else if (i++ > 0)
                        signal.Wait(millisecondsTimeout: i);
                    else
                        Thread.Yield();
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
            cancellationSource.ThrowIfCancellationRequested(cancellation);

            message = ReadOnlyMemory<byte>.Empty;
            var header = Header;
            var readOffset = header->ReadOffset;

            if (readOffset == header->WriteOffset)
                return false; // this is an empty queue

            var messageHeader = (MessageHeader*)Buffer.GetPointer(readOffset);

            // take a lock so no other thread can start processing this message
            if (Interlocked.CompareExchange(
                ref messageHeader->State,
                MessageHeader.LockedToBeConsumedState,
                MessageHeader.ReadyToBeConsumedState) != MessageHeader.ReadyToBeConsumedState)
            {
                return false; // some other subscriber got to this message before us
            }

            // was the header advanced already by another subscriber?
            if (header->ReadOffset != readOffset)
            {
                // revert the lock
                Interlocked.CompareExchange(
                    ref messageHeader->State,
                    MessageHeader.ReadyToBeConsumedState,
                    MessageHeader.LockedToBeConsumedState);

                return false;
            }

            // read the message body from the queue buffer
            var bodyLength = messageHeader->BodyLength;
            var bodyOffset = GetMessageBodyOffset(readOffset);
            message = Buffer.Read(bodyOffset, bodyLength, resultBuffer);

            // zero out the message body first
            Buffer.Clear(bodyOffset, bodyLength);

            // zero out the message header
            Buffer.Write(default(MessageHeader), readOffset);

            // updating the queue header to point the head of the queue to the next available message
            var messageLength = GetMessageLength(bodyLength);
            var newReadOffset = SafeIncrementMessageOffset(readOffset, messageLength);
            if (Interlocked.CompareExchange(ref header->ReadOffset, newReadOffset, readOffset) == readOffset)
                return true;

            throw new InvalidOperationException(
                "This is unexpected and can be a serious bug. We took a lock on this message " +
                "prior to this point which should ensure that the ReadOffset is left unchanged.");
        }
    }
}
