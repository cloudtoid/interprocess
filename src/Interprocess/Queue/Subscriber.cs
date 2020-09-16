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
            countdownEvent.Dispose();

            signal.Dispose();
            cancellationSource.Dispose();
            base.Dispose();
        }

        public bool TryDequeue(CancellationToken cancellation, out ReadOnlyMemory<byte> message)
            => TryDequeue(default(Memory<byte>?), cancellation, out message);

        public bool TryDequeue(
            Memory<byte> resultBuffer,
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message)
            => TryDequeue((Memory<byte>?)resultBuffer, cancellation, out message);

        public ReadOnlyMemory<byte> Dequeue(CancellationToken cancellation)
            => Dequeue(default(Memory<byte>?), cancellation);

        public ReadOnlyMemory<byte> Dequeue(Memory<byte> resultBuffer, CancellationToken cancellation)
            => Dequeue((Memory<byte>?)resultBuffer, cancellation);

        private ReadOnlyMemory<byte> Dequeue(Memory<byte>? resultBuffer, CancellationToken cancellation)
        {
            // Chances are that the previous reader is not fully done with reading the current message.
            // We, therefore, spin-wait before using the semaphore which is more expensive.
            // We have seen an order of magnitude performance improvement by applying this simple trick.

            for (var i = 0; i < 10; i++)
            {
                if (TryDequeue(resultBuffer, cancellation, out var message))
                    return message;

                Thread.SpinWait(10);
            }

            while (true)
            {
                if (TryDequeue(resultBuffer, cancellation, out var message))
                    return message;

                signal.Wait(millisecondsTimeout: 100);
            }
        }

        private unsafe bool TryDequeue(
            Memory<byte>? resultBuffer,
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message)
        {
            cancellation.ThrowIfCancellationRequested();
            countdownEvent.AddCount();
            message = ReadOnlyMemory<byte>.Empty;
            try
            {
                using var linkedSource = new LinkedCancellationToken(cancellationSource.Token, cancellation);
                cancellation = linkedSource.Token;

                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();

                    var header = Header;
                    var headOffset = header->HeadOffset;

                    if (headOffset == header->TailOffset)
                        return false; // this is an empty queue

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
            finally
            {
                countdownEvent.Signal();
            }
        }
    }
}
