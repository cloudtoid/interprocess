using System.Threading;

namespace Cloudtoid.Interprocess
{
    internal partial class InteprocessSignal
    {
        private sealed class WindowsSignal : InteprocessSignal
        {
            private const string HandleNamePrefix = "CT.IP.";
            private readonly Semaphore handle;

            internal WindowsSignal(string queueName)
            {
                handle = new Semaphore(0, int.MaxValue, HandleNamePrefix + queueName);
            }

            public override void Dispose()
                => handle.Dispose();

            internal override void Signal()
               => handle.Release();

            internal override bool Wait(int millisecondsTimeout)
                => handle.WaitOne(millisecondsTimeout);
        }
    }
}
