using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct QueueHeader
    {
        [FieldOffset(0)]
        internal long HeadOffset;

        [FieldOffset(8)]
        internal long TailOffset;
    }
}
