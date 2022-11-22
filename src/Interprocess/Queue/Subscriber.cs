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
            var header = *Header;

            // is there already a lock?
            if (header.ReadOffset == QueueHeader.LockedState)
                return false;

            // is this an empty queue?
            if (header.ReadOffset == header.WriteOffset)
                return false;

            // take a lock so no other thread can read a message
            var readOffset = Interlocked.Exchange(ref Header->ReadOffset, QueueHeader.LockedState);

            // did another subscriber get a lock before us?
            if (readOffset == QueueHeader.LockedState)
                return false;

            try
            {
                // is the queue empty now?
                if (readOffset == Header->WriteOffset)
                    return false;

                // now we have the lock and the queue is not empty

                var messageHeader = (MessageHeader*)Buffer.GetPointer(readOffset);

                // was this message fully written by the publisher? if not, release the lock and retry again later
                if (Interlocked.CompareExchange(
                    ref messageHeader->State,
                    MessageHeader.LockedToBeConsumedState,
                    MessageHeader.ReadyToBeConsumedState) != MessageHeader.ReadyToBeConsumedState)
                {
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
                readOffset = SafeIncrementMessageOffset(readOffset, messageLength);
            }
            finally
            {
                // Release the lock
                Interlocked.Exchange(ref Header->ReadOffset, readOffset);
            }

            return true;
        }
    }
}
