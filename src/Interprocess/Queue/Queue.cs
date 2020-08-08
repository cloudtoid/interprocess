using System;
using System.Runtime.CompilerServices;

namespace Cloudtoid.Interprocess
{
    internal abstract class Queue : IDisposable
    {
        private readonly QueueOptions options;
        private readonly MemoryView view;
        protected readonly CircularBuffer buffer;

        protected unsafe Queue(QueueOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));

            view = new MemoryView(options);
            try
            {
                buffer = new CircularBuffer(sizeof(QueueHeader) + view.Pointer, options.Capacity);
            }
            catch
            {
                view.Dispose();
                throw;
            }
        }

        public unsafe QueueHeader* Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (QueueHeader*)view.Pointer;
        }

        public virtual void Dispose()
            => view.Dispose();

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

        protected SharedAssetsIdentifier CreateIdentifier()
        {
            var path = Util.GetAbsolutePath(options.Path);
            return new SharedAssetsIdentifier(options.QueueName, path);
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
