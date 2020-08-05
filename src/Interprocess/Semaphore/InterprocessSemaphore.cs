using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.Semaphore.Unix;
using Cloudtoid.Interprocess.Semaphore.Windows;

namespace Cloudtoid.Interprocess
{
    /// <summary>
    /// This is a platform agnostic named semaphore. Named semaphores are synchronization
    /// constructs accessible across processes.
    /// </summary>
    internal sealed class InterprocessSemaphore : IInterprocessSemaphore
    {
        private readonly IInterprocessSemaphore semaphore;

        internal InterprocessSemaphore(SharedAssetsIdentifier identifier)
        {
            semaphore = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsSemaphore(identifier)
                : (IInterprocessSemaphore)new UnixSemaphore(identifier);
        }

        public void Dispose()
            => semaphore.Dispose();

        public ValueTask ReleaseAsync()
            => semaphore.ReleaseAsync();

        public bool WaitOne(int millisecondsTimeout)
            => semaphore.WaitOne(millisecondsTimeout);
    }
}
