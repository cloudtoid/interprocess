using System.Threading;

namespace Cloudtoid.Interprocess
{
    internal partial class InteprocessSignal
    {
        private sealed class WindowsSignal : InteprocessSignal
        {
            private const string HandleNamePrefix = "CT.IP.";
            private readonly EventWaitHandle handle;

            internal WindowsSignal(string queueName)
            {
                handle = new EventWaitHandle(true, EventResetMode.AutoReset, HandleNamePrefix + queueName);
            }

            public override void Dispose()
                => handle.Dispose();

            internal override void Signal()
                => handle.Set();

            internal override bool Wait(int millisecondsTimeout)
                => handle.WaitOne(millisecondsTimeout);
        }
    }
}
