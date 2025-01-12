using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess;

// We rely on this structure to fit in 64 bits.
// If you change the size of this, no longer many of the assumptions
// taken in this code are going to be valid.
[StructLayout(LayoutKind.Explicit, Size = 8)]
internal struct MessageHeader
{
    internal const int LockedToBeConsumedState = 1;
    internal const int ReadyToBeConsumedState = 2;

    [FieldOffset(0)]
    internal int State;

    [FieldOffset(4)]
    internal int BodyLength;

    internal MessageHeader(int state, int bodyLength)
    {
        State = state;
        BodyLength = bodyLength;
    }
}