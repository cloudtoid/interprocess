using System.Runtime.InteropServices;

namespace Cloudtoid.SharedMemory
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct QueueHeader
    {
        internal long HeadOffset;
        internal long TailOffset;
    }
}
