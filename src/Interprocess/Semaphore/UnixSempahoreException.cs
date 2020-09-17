using System;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    internal class UnixSempahoreException
        : Exception
    {
        public UnixSempahoreException(string message)
            : base(message)
        {
        }

        public UnixSempahoreException(int errorCode)
            : base($"Semaphore exception with inner code = {errorCode}")
        {
        }
    }

    internal class InvalidUnixSempahoreException : UnixSempahoreException
    {
        public InvalidUnixSempahoreException()
            : base($"The spoecified semaphore does not exist or it is invalid.")
        {
        }
    }

    internal class UnixSempahoreNotExistsException : UnixSempahoreException
    {
        public UnixSempahoreNotExistsException()
            : base($"The spoecified semaphore does not exist.")
        {
        }
    }

    internal class UnixSempahoreExistsException : UnixSempahoreException
    {
        public UnixSempahoreExistsException()
            : base("A sempahore with this name already exists")
        {
        }
    }

    internal class UnixSempahoreUnauthorizedAccessException : UnixSempahoreException
    {
        public UnixSempahoreUnauthorizedAccessException()
            : base("The semaphore exists, but the caller does not have permission to open it.")
        {
        }
    }
}