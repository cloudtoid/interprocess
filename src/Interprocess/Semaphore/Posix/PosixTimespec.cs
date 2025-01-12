using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess.Semaphore.Posix;

[StructLayout(LayoutKind.Sequential)]
internal struct PosixTimespec
{
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
#pragma warning disable SA1310 // Field names should not contain underscore
    public long tv_sec;   // seconds
    public long tv_nsec;  // nanoseconds
#pragma warning restore SA1307 // Accessible fields should begin with upper-case letter
#pragma warning restore SA1310 // Field names should not contain underscore

    public static implicit operator PosixTimespec(DateTimeOffset dateTime) => new()
    {
        tv_sec = dateTime.ToUnixTimeSeconds(),
        tv_nsec = 1000_000 * (dateTime.ToUnixTimeMilliseconds() % 1000)
    };
}