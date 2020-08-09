using System.Threading;
using System.Threading.Tasks;
using SysSemaphore = System.Threading.Semaphore;

namespace Cloudtoid.Interprocess.Semaphore.Windows
{
    // just a wrapper over the Windows named semaphore
    internal sealed class Semaphore :
        IInterprocessSemaphoreWaiter,
        IInterprocessSemaphoreReleaser
    {
        private const string HandleNamePrefix = "CT.IP.";
        private readonly SysSemaphore handle;

        internal Semaphore(SharedAssetsIdentifier identifier)
        {
            handle = new SysSemaphore(0, int.MaxValue, HandleNamePrefix + identifier.Name);
        }

        public void Dispose()
            => handle.Dispose();

        public Task ReleaseAsync(CancellationToken cancellation)
        {
            handle.Release();
            return Task.CompletedTask;
        }

        public bool WaitOne(int millisecondsTimeout)
            => handle.WaitOne(millisecondsTimeout);
    }
}