using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess;

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct QueueHeader
{
    /// <summary>
    /// Monotonically increasing byte position of the next message to read.
    /// </summary>
    [FieldOffset(0)]
    internal long ReadOffset;

    /// <summary>
    /// Monotonically increasing byte position of the next message to write.
    /// </summary>
    [FieldOffset(8)]
    internal long WriteOffset;

    /// <summary>
    /// Time (ticks) at which the read lock was taken. It is set to zero if not lock
    /// </summary>
    [FieldOffset(16)]
    internal long ReadLockTimestamp;

    /// <summary>
    /// Not used and might be used in the future
    /// </summary>
    [FieldOffset(24)]
    internal long Reserved;

    internal readonly bool IsEmpty() =>
        ReadOffset == WriteOffset;
}