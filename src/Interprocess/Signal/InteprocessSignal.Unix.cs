using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Signal.Unix
{
    // internal for testing
    internal sealed partial class UnixSignal : IInteprocessSignal
    {
        private const string PathSuffix = ".cloudtoid/interprocess/signal";
        private const string Extension = ".socket";

        private readonly object serverLockObject = new object();
        private readonly SharedAssetsIdentifier identifier;
        private readonly Client client;
        private Server? server;

        internal UnixSignal(SharedAssetsIdentifier identifier)
        {
            var path = Path.Combine(identifier.Path, PathSuffix);
            Directory.CreateDirectory(path);

            this.identifier = new SharedAssetsIdentifier(identifier.Name, path);
            client = new Client(identifier);
        }

        public void Dispose()
        {
            server?.Dispose();
            client.Dispose();
        }

        public bool Wait(int millisecondsTimeout)
            => client.Wait(millisecondsTimeout);

        public ValueTask SignalAsync()
        {
            if (server is null)
            {
                lock (serverLockObject)
                {
                    if (server is null)
                        server = new Server(identifier);
                }
            }

            return server.SignalAsync();
        }
    }

    internal static class Extensions
    {
        internal static void SafeDispose(this Socket? socket)
        {
            try
            {
                socket?.Dispose();
            }
            catch { }
        }
    }
}
