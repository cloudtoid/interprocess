using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct MessageHeader
    {
        [FieldOffset(0)]
        internal MessageState State;

        [FieldOffset(8)]
        internal long BodyLength;

        internal MessageHeader(MessageState state, long bodyLength)
        {
            State = state;
            BodyLength = bodyLength;
        }
    }
}
