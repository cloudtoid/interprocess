using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    internal sealed class Subscriber : Queue, ISubscriber
    {
        private static readonly long TicksForTenSeconds = TimeSpan.FromSeconds(10).Ticks;
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
            var readLockTimestamp = header.ReadLockTimestamp;
            var nowInTicks = DateTime.UtcNow.Ticks;

            // is there already a lock or has the previous lock timed out meaning that a subscriber crashed?
            if (nowInTicks - readLockTimestamp < TicksForTenSeconds)
                return false;

            // is this an empty queue?
            if (header.IsEmpty())
                return false;

            // take a read-lock so no other thread can read a message
            if (Interlocked.CompareExchange(ref Header->ReadLockTimestamp, nowInTicks, readLockTimestamp) != readLockTimestamp)
                return false;

            try
            {
                // is the queue empty now that we were able to get a read-lock?
                if (Header->IsEmpty())
                    return false;

                // now we have the lock and the queue is not empty
                var readOffset = Header->ReadOffset;
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
                message = Buffer.Read(
                    GetMessageBodyOffset(readOffset),
                    bodyLength,
                    resultBuffer);

                // zero out the message, including the message header
                var messageLength = GetMessageLength(bodyLength);
                Buffer.Clear(readOffset, messageLength);

                // update the read offset of the queue
                readOffset = SafeIncrementMessageOffset(readOffset, messageLength);
                Interlocked.Exchange(ref Header->ReadOffset, readOffset);
            }
            finally
            {
                Interlocked.Exchange(ref Header->ReadLockTimestamp, 0L);
            }

            return true;
        }
    }
}
