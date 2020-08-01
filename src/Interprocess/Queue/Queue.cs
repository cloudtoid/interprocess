using System;
using System.Runtime.CompilerServices;

namespace Cloudtoid.Interprocess
{
    internal abstract class Queue : IDisposable
    {
        private readonly InteprocessSignal receiversSignal;
        protected readonly SharedMemoryView view;
        protected readonly CircularBuffer buffer;

        protected unsafe Queue(QueueOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            try
            {
                receiversSignal = InteprocessSignal.Create(options.QueueName, options.Path);
                view = new SharedMemoryView(options);
                buffer = new CircularBuffer(sizeof(QueueHeader) + view.Pointer, options.Capacity);
            }
            catch
            {
                view?.Dispose();
                receiversSignal?.Dispose();
                throw;
            }
        }

        public virtual void Dispose()
        {
            view.Dispose();
            receiversSignal.Dispose();
        }

        /// <summary>
        /// Signals at most one receiver to attempt to see if there are any messages left in the queue.
        /// There are no guarantees that there are any messages left in the queue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SignalReceivers()
            => receiversSignal.Signal();

        /// <summary>
        /// Waits the maximum of <paramref name="millisecondsTimeout"/> for a signal that there might be
        /// more messages in the queue ready to be processed.
        /// NOTE: There are no guarantees that there are any messages left in the queue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void WaitForReceiverSignal(int millisecondsTimeout)
            => receiversSignal.Wait(millisecondsTimeout);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected unsafe long GetMessageBodyOffset(long messageHeaderOffset)
            => sizeof(MessageHeader) + messageHeaderOffset;

        /// <summary>
        /// Calculates the total length of a message which consists of [header][body][padding].
        /// <list type="bullet">
        /// <item><term>header</term><description>An instance of <see cref="MessageHeader"/></description></item>
        /// <item><term>body</term><description>A collection of bytes provided by the user</description></item>
        /// <item><term>padding</term><description>A possible padding is added to round up the length to the closest multiple of 8 bytes</description></item>
        /// </list>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected unsafe long GetMessageLength(long bodyLength)
        {
            var length = sizeof(MessageHeader) + bodyLength;

            // Round up to the closest integer divisible by 8. This will add the [padding] if one is needed.
            return 8 * (long)Math.Ceiling(length / 8.0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected static long SafeIncrementMessageOffset(long offset, long increment)
        {
            if (increment > long.MaxValue - offset)
                return -long.MaxValue + offset + increment; // Do NOT change the order of additions here

            return offset + increment;
        }
    }
}
