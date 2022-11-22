using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct QueueHeader
    {
        internal const long LockedState = -1;

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
    }
}
