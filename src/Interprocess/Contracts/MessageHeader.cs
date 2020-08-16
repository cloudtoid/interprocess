using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MessageHeader
    {
        internal MessageState State;
        internal long BodyLength;

        internal MessageHeader(MessageState state, long bodyLength)
        {
            State = state;
            BodyLength = bodyLength;
        }
    }
}
