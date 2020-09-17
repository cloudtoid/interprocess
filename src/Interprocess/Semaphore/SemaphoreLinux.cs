using System;

namespace Cloudtoid.Interprocess.Semaphore.Linux
{
    internal class SemaphoreLinux : IInterprocessSemaphoreWaiter, IInterprocessSemaphoreReleaser
    {
        private const string HandleNamePrefix = @"/ct.ip.";
        private readonly string name;
        private readonly bool deleteOnDispose;
        private readonly IntPtr handle;

        public SemaphoreLinux(string name, bool deleteOnDispose = false)
        {
            this.name = name = HandleNamePrefix + name;
            this.deleteOnDispose = deleteOnDispose;
            handle = SemaphoreLinuxInterop.CreateOrOpenSemaphore(name, 0);
        }

        ~SemaphoreLinux()
            => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            SemaphoreLinuxInterop.Close(handle);

            if (deleteOnDispose)
                SemaphoreLinuxInterop.Unlink(name);
        }

        public void Release()
            => SemaphoreLinuxInterop.Release(handle);

        public bool Wait(int millisecondsTimeout)
            => SemaphoreLinuxInterop.Wait(handle, millisecondsTimeout);
    }
}