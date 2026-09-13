namespace Cloudtoid.Interprocess;

internal interface IInterprocessSemaphoreWaiter : IInterprocessSemaphoreReleaser
{
    bool Wait(int millisecondsTimeout);
}