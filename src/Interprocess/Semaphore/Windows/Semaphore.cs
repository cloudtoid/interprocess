using SysSemaphore = System.Threading.Semaphore;

namespace Cloudtoid.Interprocess.Semaphore.Windows
{
    // just a wrapper over the Windows named semaphore
    internal sealed class Semaphore :
        IInterprocessSemaphoreWaiter,
        IInterprocessSemaphoreReleaser
    {
        private const string HandleNamePrefix = @"Global\CT.IP.";
        private readonly SysSemaphore handle;

        internal Semaphore(SharedAssetsIdentifier identifier)
        {
            handle = new SysSemaphore(0, int.MaxValue, HandleNamePrefix + identifier.Name);
        }

        public void Dispose()
            => handle.Dispose();

        public void Release()
            => handle.Release();

        public bool Wait(int millisecondsTimeout)
            => handle.WaitOne(millisecondsTimeout);
    }
}