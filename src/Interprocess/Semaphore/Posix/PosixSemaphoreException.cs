namespace Cloudtoid.Interprocess.Semaphore.Posix;

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

internal sealed class InvalidPosixSemaphoreException : PosixSemaphoreException
{
    public InvalidPosixSemaphoreException()
        : base("The specified semaphore does not exist or it is invalid.")
    {
    }
}

internal sealed class PosixSemaphoreNotExistsException : PosixSemaphoreException
{
    public PosixSemaphoreNotExistsException()
        : base("The specified semaphore does not exist.")
    {
    }
}

internal sealed class PosixSemaphoreExistsException : PosixSemaphoreException
{
    public PosixSemaphoreExistsException()
        : base("A Semaphore with this name already exists")
    {
    }
}

internal sealed class PosixSemaphoreUnauthorizedAccessException : PosixSemaphoreException
{
    public PosixSemaphoreUnauthorizedAccessException()
        : base("The semaphore exists, but the caller does not have permission to open it.")
    {
    }
}