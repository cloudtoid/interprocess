using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal interface IInterprocessSemaphoreWaiter : IDisposable
    {
        bool WaitOne(int millisecondsTimeout);
    }

    internal interface IInterprocessSemaphoreReleaser : IDisposable
    {
        Task ReleaseAsync(CancellationToken cancellation);
    }
}
