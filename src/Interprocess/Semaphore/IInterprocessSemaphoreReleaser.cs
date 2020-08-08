using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal interface IInterprocessSemaphoreReleaser : IDisposable
    {
        Task ReleaseAsync(CancellationToken cancellation);
    }
}
