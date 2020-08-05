using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.Signal.Unix;
using Cloudtoid.Interprocess.Signal.Windows;

namespace Cloudtoid.Interprocess
{
    internal sealed class InteprocessSignal : IInteprocessSignal
    {
        private readonly IInteprocessSignal signal;

        internal InteprocessSignal(SharedAssetsIdentifier identifier)
        {
            signal = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsSignal(identifier)
                : (IInteprocessSignal)new UnixSignal(identifier);
        }

        public void Dispose()
            => signal.Dispose();

        public ValueTask SignalAsync()
            => signal.SignalAsync();

        public bool Wait(int millisecondsTimeout)
            => signal.Wait(millisecondsTimeout);
    }
}
