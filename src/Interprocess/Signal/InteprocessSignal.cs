using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.Signal.Unix;
using Cloudtoid.Interprocess.Signal.Windows;

namespace Cloudtoid.Interprocess
{
    internal sealed class InteprocessSignal : IInteprocessSignal
    {
        private readonly IInteprocessSignal signal;

        internal InteprocessSignal(string queueName, string path)
        {
            signal = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsSignal(queueName)
                : (IInteprocessSignal)new UnixSignal(queueName, path);
        }

        public void Dispose()
            => signal.Dispose();

        public ValueTask SignalAsync()
            => signal.SignalAsync();

        public bool Wait(int millisecondsTimeout)
            => signal.Wait(millisecondsTimeout);
    }
}
