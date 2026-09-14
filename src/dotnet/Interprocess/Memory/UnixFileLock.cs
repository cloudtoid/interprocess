using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cloudtoid.Interprocess.Memory.Unix;

internal static partial class UnixFileLock
{
    private const int Shared = 1;
    private const int Exclusive = 2;
    private const int NonBlocking = 4;
    private const int Interrupted = 4;

    internal static SafeFileHandle AcquireDirectory(string directory)
    {
        // Lock the directory itself: its inode must stay in place while queues are active.
        // Native open avoids FileStream's implicit, nonblocking sharing locks.
        var closeOnExec = OperatingSystem.IsMacOS() ? 0x1000000
            : OperatingSystem.IsFreeBSD() ? 0x100000 : 0x80000;
        var descriptor = Open(directory, closeOnExec);
        if (descriptor < 0)
        {
            throw new IOException(
                "Could not open the queue directory.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }

        var handle = new SafeFileHandle(descriptor, ownsHandle: true);
        try
        {
            Lock(handle, Exclusive);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static void AcquireShared(SafeFileHandle handle) => Lock(handle, Shared);

    internal static bool TryAcquireExclusive(SafeFileHandle handle) => Lock(handle, Exclusive | NonBlocking);

    private static bool Lock(SafeFileHandle handle, int operation)
    {
        while (Flock(handle, operation) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == Interrupted)
                continue;

            var wouldBlock = OperatingSystem.IsLinux() ? 11 : 35;
            if ((operation & NonBlocking) != 0 && error == wouldBlock)
                return false;

            throw new IOException("Could not lock the queue resources.", new Win32Exception(error));
        }

        return true;
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(SafeFileHandle handle, int operation);
}