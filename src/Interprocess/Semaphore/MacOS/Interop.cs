using System.Runtime.InteropServices;
using Cloudtoid.Interprocess.Semaphore.Posix;

namespace Cloudtoid.Interprocess.Semaphore.MacOS;

internal static partial class Interop
{
    private const string Lib = "libSystem.dylib";
    private const int SEMVALUEMAX = 32767;
    private const int OCREAT = 0x0200;  // create the semaphore if it does not exist

    private const int ENOENT = 2;        // The named semaphore does not exist.
    private const int EINTR = 4;         // Semaphore operation was interrupted by a signal.
    private const int EDEADLK = 11;      // A deadlock was detected.
    private const int ENOMEM = 12;       // Out of memory
    private const int EACCES = 13;       // Semaphore exists, but the caller does not have permission to open it.
    private const int EEXIST = 17;       // O_CREAT and O_EXCL were specified and the semaphore exists.
    private const int EINVAL = 22;       // Invalid semaphore or operation on a semaphore
    private const int ENFILE = 23;       // Too many semaphores or file descriptors are open on the system.
    private const int EMFILE = 24;       // The process has already reached its limit for semaphores or file descriptors in use.
    private const int EAGAIN = 35;       // The semaphore is already locked.
    private const int ENAMETOOLONG = 63; // The specified semaphore name is too long
    private const int EOVERFLOW = 84;    // The maximum allowable value for a semaphore would be exceeded.

    private static readonly IntPtr SemFailed = new(-1);

    private static unsafe int Error => Marshal.GetLastWin32Error();

    internal static IntPtr CreateOrOpenSemaphore(string name, uint initialCount)
    {
        var handle = SemaphoreOpen(
            name, OCREAT, 0, 0, 0, 0, 0, 0, (uint)PosixFilePermissions.ACCESSPERMS, initialCount);

        if (handle != SemFailed)
            return handle;

        throw Error switch
        {
            EINVAL => new ArgumentException(
                $"One of the arguments passed to sem_open is invalid. Please also ensure {nameof(initialCount)} is less than {SEMVALUEMAX}."),
            ENAMETOOLONG => new ArgumentException("The specified semaphore name is too long.", nameof(name)),
            EACCES => new PosixSemaphoreUnauthorizedAccessException(),
            EEXIST => new PosixSemaphoreExistsException(),
            EINTR => new OperationCanceledException(),
            ENFILE => new PosixSemaphoreException("Too many semaphores or file descriptors are open on the system."),
            EMFILE => new PosixSemaphoreException("Too many semaphores or file descriptors are open by this process."),
            ENOMEM => new InsufficientMemoryException(),
            _ => new PosixSemaphoreException(Error),
        };
    }

    internal static void Release(IntPtr handle)
    {
        if (SemaphorePost(handle) == 0)
            return;

        throw Error switch
        {
            EINVAL => new InvalidPosixSemaphoreException(),
            EOVERFLOW => new SemaphoreFullException(),
            _ => new PosixSemaphoreException(Error),
        };
    }

    internal static void Close(IntPtr handle)
    {
        if (SemaphoreClose(handle) == 0)
            return;

        throw Error switch
        {
            EINVAL => new InvalidPosixSemaphoreException(),
            _ => new PosixSemaphoreException(Error),
        };
    }

    internal static void Unlink(string name)
    {
        if (SemaphoreUnlink(name) == 0)
            return;

        throw Error switch
        {
            ENAMETOOLONG => new ArgumentException("The specified semaphore name is too long.", nameof(name)),
            EACCES => new PosixSemaphoreUnauthorizedAccessException(),
            ENOENT => new PosixSemaphoreNotExistsException(),
            _ => new PosixSemaphoreException(Error),
        };
    }

    internal static bool Wait(IntPtr handle, int millisecondsTimeout)
    {
        if (millisecondsTimeout == Timeout.Infinite)
        {
            Wait(handle);
        }
        else
        {
            var stopwatch = ValueStopwatch.StartNew();
            while (!TryWait(handle))
            {
                if (stopwatch.GetElapsedTime().TotalMilliseconds > millisecondsTimeout)
                    return false;

                Thread.Yield();
            }
        }

        return true;
    }

    private static void Wait(IntPtr handle)
    {
        if (SemaphoreWait(handle) == 0)
            return;

        throw Error switch
        {
            EINVAL => new InvalidPosixSemaphoreException(),
            EDEADLK => new PosixSemaphoreException("A deadlock was detected attempting to wait on a semaphore."),
            EINTR => new OperationCanceledException(),
            _ => new PosixSemaphoreException(Error),
        };
    }

    private static bool TryWait(IntPtr handle)
    {
        if (SemaphoreTryWait(handle) == 0)
            return true;

        return Error switch
        {
            EAGAIN => false,
            EINVAL => throw new InvalidPosixSemaphoreException(),
            EDEADLK => throw new PosixSemaphoreException("A deadlock was detected attempting to wait on a semaphore."),
            EINTR => throw new OperationCanceledException(),
            _ => throw new PosixSemaphoreException(Error),
        };
    }

    [LibraryImport(Lib, EntryPoint = "sem_open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr SemaphoreOpen(
        string name,
        int oflag,
        ulong __x2,
        ulong __x3,
        ulong __x4,
        ulong __x5,
        ulong __x6,
        ulong __x7,
        ulong mode,
        uint value);

    [LibraryImport(Lib, EntryPoint = "sem_post", SetLastError = true)]
    private static partial int SemaphorePost(IntPtr handle);

    [LibraryImport(Lib, EntryPoint = "sem_wait", SetLastError = true)]
    private static partial int SemaphoreWait(IntPtr handle);

    [LibraryImport(Lib, EntryPoint = "sem_trywait", SetLastError = true)]
    private static partial int SemaphoreTryWait(IntPtr handle);

    [LibraryImport(Lib, EntryPoint = "sem_unlink", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SemaphoreUnlink(string name);

    [LibraryImport(Lib, EntryPoint = "sem_close", SetLastError = true)]
    private static partial int SemaphoreClose(IntPtr handle);
}