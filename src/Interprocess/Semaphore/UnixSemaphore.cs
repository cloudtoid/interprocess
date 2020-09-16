using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cloudtoid.Interprocess
{
    internal class UnixSemaphore : IInterprocessSemaphoreWaiter, IInterprocessSemaphoreReleaser
    {
        private const string HandleNamePrefix = @"/ct.ip.";
        private readonly string name;
        private readonly bool deleteOnDispose;
        private readonly int handle;

        public UnixSemaphore(string name, bool deleteOnDispose = false)
        {
            this.name = name = HandleNamePrefix + name;
            this.deleteOnDispose = deleteOnDispose;
            handle = Interop.CreateOrOpenSemaphore(name, 0);
        }

        ~UnixSemaphore()
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
    }

    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1310:Field names should not contain underscore", Justification = "Matching the exact names in Linux/MacOS")]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:Element should begin with upper-case letter", Justification = "Matching the exact names in Linux/MacOS")]
    [SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1513:Closing brace should be followed by blank line", Justification = "There is a bug in the rule!")]
    internal static class Interop
    {
        private const int O_CREATE = 0x200; // create the semaphore if it does not exist
        private const int SEM_VALUE_MAX = 32767;
        private const int SEM_FAILED = -1;

        /// <summary>
        /// A deadlock was detected.
        /// </summary>
        internal const int EDEADLK = 11;

        /// <summary>
        /// The required permissions (for reading and/or writing) are denied for the given flags; or O_CREAT is specified,
        /// the object does not exist, and permission to create the semaphore is denied.
        /// </summary>
        internal const int EACCES = 13;

        /// <summary>
        /// O_CREAT and O_EXCL were specified and the semaphore exists.
        /// </summary>
        internal const int EEXIST = 17;

        /// <summary>
        /// The sem_open() operation was interrupted by a signal.
        /// </summary>
        internal const int EINTR = 4;

        /// <summary>
        /// The shm_open() operation is not supported; or O_CREAT is specified and value exceeds SEM_VALUE_MAX.
        /// </summary>
        internal const int EINVAL = 22;

        /// <summary>
        /// The process has already reached its limit for semaphores or file descriptors in use.
        /// </summary>
        internal const int EMFILE = 24;

        /// <summary>
        /// Too many semaphores or file descriptors are open on the system.
        /// </summary>
        internal const int ENFILE = 23;

        /// <summary>
        /// O_CREAT is specified, the file does not exist, and there is insufficient space available to create the semaphore.
        /// </summary>
        internal const int ENOSPC = 28; // No space left on device

        /// <summary>
        /// The semaphore is already locked.
        /// </summary>
        internal const int EAGAIN = 35;

        /// <summary>
        ///  O_CREAT is not set and the named semaphore does not exist.
        /// </summary>
        internal const int ENOENT = 2;  // No such file or directory

        /// <summary>
        /// name exceeded PSEMNAMLEN characters.
        /// </summary>
        private const int ENAMETOOLONG = 63; // File name too long

        private static unsafe int errno => Marshal.GetLastWin32Error();

        internal static int CreateOrOpenSemaphore(string name, uint initialCount)
        {
            // 777 == Read, Write, and Execute permissions for Owner, Group, and Others
            var handle = Linux.sem_open(name, O_CREATE, 777, initialCount);
            if (handle != SEM_FAILED)
                return handle;

            throw errno switch
            {
                EINVAL => new ArgumentException($"The initial count cannot be greater than {SEM_VALUE_MAX}.", nameof(initialCount)),
                ENAMETOOLONG => new ArgumentException($"The specified semaphore name is too long.", nameof(name)),
                EACCES => new SempahoreUnauthorizedAccessException(),
                EEXIST => new SempahoreExistsException(),
                EINTR => new SempahoreException("The sem_open() operation was interrupted by a signal.", EINTR),
                ENFILE => new SempahoreException("Too many semaphores or file descriptors are open on the system.", ENFILE),
                _ => new SempahoreException(errno),
            };
        }

        internal static void Post(int handle)
        {
            if (Linux.sem_post(handle) == 0)
                return;

            throw errno switch
            {
                EINVAL => new InvalidSempahoreException(),
                _ => new SempahoreException(errno),
            };
        }

        internal static void Wait(int handle)
        {
            if (Linux.sem_wait(handle) == 0)
                return;

            throw errno switch
            {
                EINVAL => new InvalidSempahoreException(),
                EDEADLK => new SempahoreException($"A deadlock was detected attempting to wait on a semaphore.", EDEADLK),
                EINTR => new OperationCanceledException(),
                _ => new SempahoreException(errno),
            };
        }

        internal static bool TryWait(int handle)
        {
            if (Linux.sem_trywait(handle) == 0)
                return true;

            return errno switch
            {
                EAGAIN => false,
                EINVAL => throw new InvalidSempahoreException(),
                EDEADLK => throw new SempahoreException($"A deadlock was detected attempting to wait on a semaphore.", EDEADLK),
                EINTR => throw new OperationCanceledException(),
                _ => throw new SempahoreException(errno),
            };
        }

        internal static bool Wait(int handle, int millisecondsTimeout)
        {
            if (millisecondsTimeout == Timeout.Infinite)
            {
                Wait(handle);
            }
            else
            {
                var start = DateTime.Now;
                while (!TryWait(handle))
                {
                    if ((DateTime.Now - start).Milliseconds > millisecondsTimeout)
                        return false;

                    Thread.Yield();
                }
            }

            return true;
        }

        internal static void Close(int handle)
        {
            if (Linux.sem_close(handle) == 0)
                return;

            throw errno switch
            {
                EINVAL => new InvalidSempahoreException(),
                _ => new SempahoreException(errno),
            };
        }

        internal static void Unlink(string name)
        {
            if (Linux.sem_unlink(name) == 0)
                return;

            throw errno switch
            {
                ENAMETOOLONG => new ArgumentException($"The specified semaphore name is too long.", nameof(name)),
                EACCES => new SempahoreUnauthorizedAccessException(),
                ENOENT => new SempahoreNotExistsException(),
                _ => new SempahoreException(errno),
            };
        }

        private static class OsX
        {
            private const string LibC = "libSystem.dylib";

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_open(string name, int mode, ushort permission, uint initialCount);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_post(int handle);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_wait(int handle);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_trywait(int handle);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_unlink(string name);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_close(int handle);
        }

        private static class Linux
        {
            private const string LibC = "libc.so.6";

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_open(string name, int mode, ushort permission, uint initialCount);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_post(int handle);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_wait(int handle);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_trywait(int handle);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_unlink(string name);

            [DllImport(LibC, SetLastError = true)]
            internal static extern int sem_close(int handle);
        }
    }

    internal class SempahoreException : Exception
    {
        public SempahoreException(string message, int errorCode)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public SempahoreException(int errorCode)
            : base()
        {
            ErrorCode = errorCode;
        }

        public int ErrorCode { get; }
    }

    internal class InvalidSempahoreException : SempahoreException
    {
        public InvalidSempahoreException()
            : base($"The spoecified semaphore does not exist or it is invalid.", Interop.EINVAL)
        {
        }
    }

    internal class SempahoreNotExistsException : SempahoreException
    {
        public SempahoreNotExistsException()
            : base($"The spoecified semaphore does not exist.", Interop.ENOENT)
        {
        }
    }

    internal class SempahoreExistsException : SempahoreException
    {
        public SempahoreExistsException()
            : base("A sempahore with this name already exists", Interop.EEXIST)
        {
        }
    }

    internal class SempahoreUnauthorizedAccessException : SempahoreException
    {
        public SempahoreUnauthorizedAccessException()
            : base("The required permissions (for reading and/or writing) are denied for the given flags; or O_CREAT is specified, the object does not exist, and permission to create the semaphore is denied.", Interop.EACCES)
        {
        }
    }
}