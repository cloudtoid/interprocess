using System.Threading;
using System.Threading.Tasks;
using WinSemaphore = System.Threading.Semaphore;

namespace Cloudtoid.Interprocess.Semaphore.Windows
{
    // just a wrapper over the Windows named semaphore
    internal sealed class WindowsSemaphore : IInterprocessSemaphore
    {
        private const string HandleNamePrefix = "CT.IP.";
        private readonly WinSemaphore handle;

        internal WindowsSemaphore(SharedAssetsIdentifier identifier)
        {
            handle = new WinSemaphore(0, int.MaxValue, HandleNamePrefix + identifier.Name);
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