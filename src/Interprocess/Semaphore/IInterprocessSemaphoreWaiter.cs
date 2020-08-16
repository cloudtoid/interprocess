using System;

namespace Cloudtoid.Interprocess
{
    internal interface IInterprocessSemaphoreWaiter : IDisposable
    {
        bool WaitOne(int millisecondsTimeout);
    }
}
