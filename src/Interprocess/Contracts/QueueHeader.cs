using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct QueueHeader
    {
        /// <summary>
        /// Where the next message could potentially be read
        /// </summary>
        [FieldOffset(0)]
        internal long ReadOffset;

        /// <summary>
        /// Where the next message could potentially be written
        /// </summary>
        [FieldOffset(8)]
        internal long WriteOffset;

        /// <summary>
        /// Time (tiks) at which the read lock was taken. It is set to zero if not lock
        /// </summary>
        [FieldOffset(16)]
        internal long ReadLockTimestamp;

        /// <summary>
        /// Not used and might be used in the future
        /// </summary>
        [FieldOffset(24)]
        internal long Reserved;

        internal bool IsEmpty()
            => ReadOffset == WriteOffset;
    }
}
