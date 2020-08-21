using System;

namespace Cloudtoid.Interprocess
{
    internal interface IInterprocessSemaphoreWaiter : IDisposable
    {
        bool Wait(int millisecondsTimeout);
    }
}
