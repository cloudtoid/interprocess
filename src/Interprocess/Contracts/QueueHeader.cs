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
    /// One when a notification permit is pending or a participant is about to post it.
    /// </summary>
    [FieldOffset(24)]
    internal int NotificationPending;

    /// <summary>
    /// Reserved for future use.
    /// </summary>
    [FieldOffset(28)]
    internal int Reserved;

    internal readonly bool IsEmpty() =>
        ReadOffset == WriteOffset;
}