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
    /// The unique subscriber registration holding the read lock, or zero.
    /// </summary>
    [FieldOffset(16)]
    internal long ReadLockOwner;

    /// <summary>
    /// One when a notification permit is pending or a participant is about to post it.
    /// </summary>
    [FieldOffset(24)]
    internal int NotificationPending;

    /// <summary>
    /// Last subscriber registration allocated in this queue lifetime.
    /// </summary>
    [FieldOffset(28)]
    internal int LastReaderId;

    internal readonly bool IsEmpty() =>
        ReadOffset == WriteOffset;
}