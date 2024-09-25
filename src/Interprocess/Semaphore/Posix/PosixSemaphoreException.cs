using System;

namespace Cloudtoid.Interprocess.Semaphore.Posix
{
    internal class PosixSemaphoreException
        : Exception
    {
        public PosixSemaphoreException(string message)
            : base(message)
        {
        }

        public PosixSemaphoreException(int errorCode)
            : base($"Semaphore exception with inner code = {errorCode}")
        {
        }
    }

    internal class InvalidPosixSemaphoreException : PosixSemaphoreException
    {
        public InvalidPosixSemaphoreException()
            : base($"The specified semaphore does not exist or it is invalid.")
        {
        }
    }

    internal class PosixSemaphoreNotExistsException : PosixSemaphoreException
    {
        public PosixSemaphoreNotExistsException()
            : base($"The specified semaphore does not exist.")
        {
        }
    }

    internal class PosixSemaphoreExistsException : PosixSemaphoreException
    {
        public PosixSemaphoreExistsException()
            : base("A Semaphore with this name already exists")
        {
        }
    }

    internal class PosixSemaphoreUnauthorizedAccessException : PosixSemaphoreException
    {
        public PosixSemaphoreUnauthorizedAccessException()
            : base("The semaphore exists, but the caller does not have permission to open it.")
        {
        }
    }
}