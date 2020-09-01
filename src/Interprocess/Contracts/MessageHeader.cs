using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    internal struct MessageHeader
    {
        [FieldOffset(0)]
        internal MessageState State;

        [FieldOffset(4)]
        internal int BodyLength;

        internal MessageHeader(MessageState state, int bodyLength)
        {
            State = state;
            BodyLength = bodyLength;
        }
    }
}
