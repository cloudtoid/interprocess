using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    internal sealed class Subscriber : Queue, ISubscriber
    {
        private const long BeingCreated = (long)MessageState.BeingCreated;
        private const long LockedToBeConsumed = (long)MessageState.LockedToBeConsumed;
        private const long ReadyToBeConsumed = (long)MessageState.ReadyToBeConsumed;
        private readonly IInterprocessSemaphoreWaiter signal;

        internal Subscriber(QueueOptions options, ILoggerFactory loggerFactory)
            : base(options, loggerFactory)
        {
            signal = InterprocessSemaphore.CreateWaiter(Identifier, loggerFactory);
        }

        public override void Dispose()
        {
            signal.Dispose();
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

        private unsafe bool TryDequeue(
            Memory<byte>? resultBuffer,
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message)
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();

                var header = Header;
                var headOffset = header->HeadOffset;

                // is this is an empty queue?
                if (headOffset == header->TailOffset)
                {
                    message = ReadOnlyMemory<byte>.Empty;
                    return false;
                }

                var state = (long*)Buffer.GetPointer(headOffset);

                if (*state == LockedToBeConsumed)
                    continue; // some other receiver got to this message before us

                // is the message still being written/created?
                if (*state != ReadyToBeConsumed)
                    continue; // message is not ready to be consumed yet

                // take a lock so no other thread can start processing this message
                if (Interlocked.CompareExchange(ref *state, LockedToBeConsumed, ReadyToBeConsumed) != ReadyToBeConsumed)
                    continue; // some other receiver got to this message before us

                // read the message body from the queue buffer
                var bodyOffset = GetMessageBodyOffset(headOffset);
                var bodyLength = ReadMessageBodyLength(headOffset);
                message = Buffer.Read(bodyOffset, bodyLength, resultBuffer);

                // zero out the entire message block
                var messageLength = GetMessageLength(bodyLength);
                Buffer.ZeroBlock(headOffset, messageLength);

                // updating the queue header to point the head of the queue to the next available message
                var newHeadOffset = SafeIncrementMessageOffset(headOffset, messageLength);
                var currentHeadOffset = (long*)header;
                Interlocked.Exchange(ref *currentHeadOffset, newHeadOffset);

                return true;
            }
        }

        private ReadOnlyMemory<byte> Dequeue(
            Memory<byte>? resultBuffer,
            CancellationToken cancellation)
        {
            var wait = false;

            while (true)
            {
                if (wait)
                    signal.Wait(millisecondsTimeout: 100);
                else
                    wait = false;

                if (TryDequeue(resultBuffer, cancellation, out var message))
                    return message;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ReadMessageBodyLength(long messageHeaderOffset)
            => Buffer.ReadInt64(messageHeaderOffset + sizeof(long));
    }
}
