using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal sealed class Subscriber : Queue, ISubscriber
    {
        private const long BeingCreated = (long)MessageState.BeingCreated;
        private const long LockedToBeConsumed = (long)MessageState.LockedToBeConsumed;
        private const long ReadyToBeConsumed = (long)MessageState.ReadyToBeConsumed;

        internal Subscriber(QueueOptions options)
            : base(options)
        {
        }

        public unsafe Task<bool> TryDequeueAsync(
            CancellationToken cancellationToken,
            out ReadOnlyMemory<byte> message)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var header = *(QueueHeader*)view.Pointer;
                var headOffset = header.HeadOffset;

                // is this is an empty queue?
                if (headOffset == header.TailOffset)
                {
                    message = ReadOnlyMemory<byte>.Empty;
                    return Task.FromResult(false);
                }

                var state = (long*)buffer.GetPointer(headOffset);

                if (*state == LockedToBeConsumed)
                    continue; // some other receiver got to this message before us

                // is the message still being written/created?
                if (*state != ReadyToBeConsumed)
                {
                    Task.Delay(1).Wait();
                    if (*state != ReadyToBeConsumed)
                        continue; // message is not ready to be consumed yet
                }

                // take a lock so no other thread can start processing this message
                if (Interlocked.CompareExchange(ref *state, LockedToBeConsumed, ReadyToBeConsumed) != ReadyToBeConsumed)
                    continue; // some other receiver got to this message before us

                // read the message body from the queue buffer
                var bodyOffset = GetMessageBodyOffset(headOffset);
                var bodyLength = ReadMessageBodyLength(headOffset);
                message = buffer.Read(bodyOffset, bodyLength).AsMemory();

                // zero out the entire message block
                long messageLength = GetMessageLength(bodyLength);
                buffer.ZeroBlock(headOffset, messageLength);

                // updating the queue header to point the head of the queue to the next available message 
                var newHeadOffset = SafeIncrementMessageOffset(headOffset, messageLength);
                var currentHeadOffset = (long*)view.Pointer;
                Interlocked.Exchange(ref *currentHeadOffset, newHeadOffset);

                // signal the receivers to try and read the next message (if one is available)
                SignalReceivers();

                return Task.FromResult(true);
            }
        }

        public async Task<ReadOnlyMemory<byte>> WaitDequeueAsync(CancellationToken cancellationToken)
        {
            bool shouldWait = false;

            while (true)
            {
                if (shouldWait)
                    WaitForReceiverSignal(millisecondsTimeout: 100);
                else
                    shouldWait = true;

                if (await TryDequeueAsync(cancellationToken, out var message))
                    return message;

            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ReadMessageBodyLength(long messageHeaderOffset)
            => buffer.ReadInt64(messageHeaderOffset + sizeof(long));
    }
}
