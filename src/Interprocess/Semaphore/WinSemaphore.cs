using System.Threading;

namespace Cloudtoid.Interprocess
{
    // just a wrapper over the Windows named semaphore
    internal sealed class WinSemaphore : IInterprocessSemaphoreWaiter, IInterprocessSemaphoreReleaser
    {
        private const string HandleNamePrefix = @"Global\CT.IP.";
        private readonly Semaphore handle;

        internal WinSemaphore(string name)
        {
            handle = new Semaphore(0, int.MaxValue, HandleNamePrefix + name);
        }

        public void Dispose()
            => handle.Dispose();

        public void Release()
            => handle.Release();

        public bool Wait(int millisecondsTimeout)
            => handle.WaitOne(millisecondsTimeout);
    }
}