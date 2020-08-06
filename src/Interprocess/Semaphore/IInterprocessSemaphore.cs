using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal interface IInterprocessSemaphore : IDisposable
    {
        Task ReleaseAsync(CancellationToken cancellation);
        bool WaitOne(int millisecondsTimeout);
    }
}
