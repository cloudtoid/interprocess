using System;
using System.Runtime.CompilerServices;
using System.Threading;

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

        public unsafe bool TryDequeue(CancellationToken cancellationToken, out ReadOnlyMemory<byte> message)
        {
            bool shouldWait = false;

            while (true)
            {
                if (shouldWait)
                    WaitForReceiverSignal(millisecondsTimeout: 100);
                else
                    shouldWait = true;

                cancellationToken.ThrowIfCancellationRequested();

                var header = *(QueueHeader*)view.Pointer;
                var headOffset = header.HeadOffset;

                if (headOffset == header.TailOffset)
                    continue; // this is an empty queue

                var state = (long*)buffer.GetPointer(headOffset);

                if (*state == LockedToBeConsumed)
                    continue; // some other receiver got to this message before us

                // wait until the message is fully created/written
                WaitForMessageToBeConsumable(state, cancellationToken);

                // take a lock so no other thread can start processing this message
                if (Interlocked.CompareExchange(ref *state, LockedToBeConsumed, ReadyToBeConsumed) != ReadyToBeConsumed)
                    continue; // some other receiver got to this message before us

                // read the message body from the queue buffer
                var bodyOffset = GetMessageBodyOffset(headOffset);
                var bodyLength = ReadMessageBodyLength(headOffset);
                message = buffer.Read(bodyOffset, bodyLength);

                // updating the queue header to point the head of the queue to the next available message 
                long messageLength = GetMessageLength(bodyLength);
                var newHeadOffset = SafeIncrementMessageOffset(headOffset, messageLength);
                var currentHeadOffset = (long*)view.Pointer;
                Interlocked.Exchange(ref *currentHeadOffset, newHeadOffset);

                // signal the receivers to try and read the next message (if one is available)
                SignalReceivers();

                return true;
            }
        }

        private unsafe void WaitForMessageToBeConsumable(long* state, CancellationToken cancellationToken)
        {
            var start = DateTime.Now;
            while (*state == BeingCreated)
            {
                if ((DateTime.Now - start).Seconds > 30)
                {
                    // if we get here, we are in a bad state.
                    // treat this as a fatal exception and crash the process
                    Environment.FailFast("Trying to dequeue from the shared memory queue failed. This means that the shared memory is corrupted.");
                }

                Thread.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ReadMessageBodyLength(long messageHeaderOffset)
            => buffer.ReadInt64(messageHeaderOffset + sizeof(long));
    }
}
