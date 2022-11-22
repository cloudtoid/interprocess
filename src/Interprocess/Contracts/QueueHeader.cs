using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
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
    }
}
