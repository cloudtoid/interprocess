using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using Cloudtoid.Interprocess.Semaphore.Linux;

namespace Cloudtoid.Interprocess.Semaphore.OSX
{
    internal class SemaphoreOSX : IInterprocessSemaphoreWaiter, IInterprocessSemaphoreReleaser
    {
        private const string HandleNamePrefix = @"/ct.ip.";
        private readonly string name;
        private readonly bool deleteOnDispose;
        private readonly IntPtr handle;

        public SemaphoreOSX(string name, bool deleteOnDispose = false)
        {
            this.name = name = HandleNamePrefix + name;
            this.deleteOnDispose = deleteOnDispose;
            handle = Interop.CreateOrOpenSemaphore(name, 0);
        }

        ~SemaphoreOSX()
            => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            Interop.Close(handle);

            if (deleteOnDispose)
                Interop.Unlink(name);
        }

        public void Release()
            => Interop.Post(handle);

        public bool Wait(int millisecondsTimeout)
            => Interop.Wait(handle, millisecondsTimeout);

        [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1310:Field names should not contain underscore", Justification = "Matching the exact names in Linux/MacOS")]
        [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:Element should begin with upper-case letter", Justification = "Matching the exact names in Linux/MacOS")]
        [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1307:Accessible fields should begin with upper-case letter", Justification = "Matching the exact names in Linux/MacOS")]
        [SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1513:Closing brace should be followed by blank line", Justification = "There is a bug in the rule!")]
        private static class Interop
        {
            private const string Lib = "librt";
            private const int O_CREAT = 0x040; // create the semaphore if it does not exist
            private const int SEM_VALUE_MAX = 32767;

            /// <summary>
            ///  O_CREAT is not set and the named semaphore does not exist.
            /// </summary>
            private const int ENOENT = 2;

            /// <summary>
            /// The sem_open() operation was interrupted by a signal.
            /// </summary>
            private const int EINTR = 4;

            /// <summary>
            /// Out of memory
            /// </summary>
            private const int ENOMEM = 12;

            /// <summary>
            /// The required permissions (for reading and/or writing) are denied for the given flags; or O_CREAT is specified,
            /// the object does not exist, and permission to create the semaphore is denied.
            /// </summary>
            private const int EACCES = 13;

            /// <summary>
            /// O_CREAT and O_EXCL were specified and the semaphore exists.
            /// </summary>
            private const int EEXIST = 17;

            /// <summary>
            /// The shm_open() operation is not supported; or O_CREAT is specified and value exceeds SEM_VALUE_MAX.
            /// </summary>
            internal const int EINVAL = 22;

            /// <summary>
            /// The process has already reached its limit for semaphores or file descriptors in use.
            /// </summary>
            private const int EMFILE = 24;

            /// <summary>
            /// Too many semaphores or file descriptors are open on the system.
            /// </summary>
            private const int ENFILE = 23;

            /// <summary>
            /// name exceeded PSEMNAMLEN characters.
            /// </summary>
            private const int ENAMETOOLONG = 36;

            /// <summary>
            /// The maximum allowable value for a semaphore would be exceeded.
            /// </summary>
            private const int EOVERFLOW = 75;

            /// <summary>
            /// The call timed out before the semaphore could be locked.
            /// </summary>
            private const int ETIMEDOUT = 110;

            [Flags]
            private enum FilePermissions : uint
            {
                S_ISUID = 0x0800, // Set user ID on execution
                S_ISGID = 0x0400, // Set group ID on execution
                S_ISVTX = 0x0200, // Save swapped text after use (sticky).
                S_IRUSR = 0x0100, // Read by owner
                S_IWUSR = 0x0080, // Write by owner
                S_IXUSR = 0x0040, // Execute by owner
                S_IRGRP = 0x0020, // Read by group
                S_IWGRP = 0x0010, // Write by group
                S_IXGRP = 0x0008, // Execute by group
                S_IROTH = 0x0004, // Read by other
                S_IWOTH = 0x0002, // Write by other
                S_IXOTH = 0x0001, // Execute by other

                S_IRWXG = S_IRGRP | S_IWGRP | S_IXGRP,
                S_IRWXU = S_IRUSR | S_IWUSR | S_IXUSR,
                S_IRWXO = S_IROTH | S_IWOTH | S_IXOTH,
                ACCESSPERMS = S_IRWXU | S_IRWXG | S_IRWXO, // 0777
                ALLPERMS = S_ISUID | S_ISGID | S_ISVTX | S_IRWXU | S_IRWXG | S_IRWXO, // 07777
                DEFFILEMODE = S_IRUSR | S_IWUSR | S_IRGRP | S_IWGRP | S_IROTH | S_IWOTH, // 0666
            }

            private static unsafe int errno => Marshal.GetLastWin32Error();

            [DllImport(Lib, SetLastError = true)]
            private static extern IntPtr sem_open([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int oflag, uint mode, uint value);

            [DllImport(Lib, SetLastError = true)]
            private static extern int sem_post(IntPtr handle);

            [DllImport(Lib, SetLastError = true)]
            private static extern int sem_wait(IntPtr handle);

            [DllImport(Lib, SetLastError = true)]
            private static extern int sem_timedwait(IntPtr handle, ref Timespec abs_timeout);

            [DllImport(Lib, SetLastError = true)]
            private static extern int sem_unlink([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

            [DllImport(Lib, SetLastError = true)]
            private static extern int sem_close(IntPtr handle);

            internal static IntPtr CreateOrOpenSemaphore(string name, uint initialCount)
            {
                var handle = sem_open(name, O_CREAT, (uint)FilePermissions.ACCESSPERMS, initialCount);
                if (handle != IntPtr.Zero)
                    return handle;

                throw errno switch
                {
                    EINVAL => new ArgumentException($"The initial count cannot be greater than {SEM_VALUE_MAX}.", nameof(initialCount)),
                    ENAMETOOLONG => new ArgumentException($"The specified semaphore name is too long.", nameof(name)),
                    EACCES => new SempahoreUnauthorizedAccessException(),
                    EEXIST => new SempahoreExistsException(),
                    EINTR => new OperationCanceledException(),
                    ENFILE => new SempahoreException("Too many semaphores or file descriptors are open on the system."),
                    EMFILE => new SempahoreException("Too many semaphores or file descriptors are open by the process."),
                    ENOMEM => new InsufficientMemoryException(),
                    _ => new SempahoreException(errno),
                };
            }

            internal static void Post(IntPtr handle)
            {
                if (sem_post(handle) == 0)
                    return;

                throw errno switch
                {
                    EINVAL => new InvalidSempahoreException(),
                    EOVERFLOW => new SemaphoreFullException(),
                    _ => new SempahoreException(errno),
                };
            }

            internal static void Wait(IntPtr handle)
            {
                if (sem_wait(handle) == 0)
                    return;

                throw errno switch
                {
                    EINVAL => new InvalidSempahoreException(),
                    EINTR => new OperationCanceledException(),
                    _ => new SempahoreException(errno),
                };
            }

            internal static bool Wait(IntPtr handle, int millisecondsTimeout)
            {
                if (millisecondsTimeout == Timeout.Infinite)
                {
                    Wait(handle);
                    return true;
                }

                var timespec = new Timespec
                {
                    tv_sec = millisecondsTimeout / 1000,
                    tv_nsec = 1000_000 * (millisecondsTimeout % 1000),
                };

                if (sem_timedwait(handle, ref timespec) == 0)
                    return true;

                return errno switch
                {
                    ETIMEDOUT => false,
                    EINVAL => throw new InvalidSempahoreException(),
                    EINTR => throw new OperationCanceledException(),
                    _ => throw new SempahoreException(errno),
                };
            }

            internal static void Close(IntPtr handle)
            {
                if (sem_close(handle) == 0)
                    return;

                throw errno switch
                {
                    EINVAL => new InvalidSempahoreException(),
                    _ => new SempahoreException(errno),
                };
            }

            internal static void Unlink(string name)
            {
                if (sem_unlink(name) == 0)
                    return;

                throw errno switch
                {
                    ENAMETOOLONG => new ArgumentException($"The specified semaphore name is too long.", nameof(name)),
                    EACCES => new SempahoreUnauthorizedAccessException(),
                    ENOENT => new SempahoreNotExistsException(),
                    _ => new SempahoreException(errno),
                };
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct Timespec
            {
                public long tv_sec;   // seconds
                public long tv_nsec;  // nanoseconds
            }
        }
    }
}