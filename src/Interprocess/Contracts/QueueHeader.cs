using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct QueueHeader
    {
        internal long HeadOffset;
        internal long TailOffset;
    }
}
