using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Signal.Windows
{
    internal sealed class WindowsSignal : IInteprocessSignal
    {
        private const string HandleNamePrefix = "CT.IP.";
        private readonly EventWaitHandle handle;

        internal WindowsSignal(string queueName)
        {
            handle = new EventWaitHandle(true, EventResetMode.AutoReset, HandleNamePrefix + queueName);
        }

        public void Dispose()
            => handle.Dispose();

        public ValueTask SignalAsync()
        {
            handle.Set();
            return new ValueTask();
        }

        public bool Wait(int millisecondsTimeout)
            => handle.WaitOne(millisecondsTimeout);
    }
}