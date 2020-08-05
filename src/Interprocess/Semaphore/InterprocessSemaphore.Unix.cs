using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    /// <summary>
    /// 
    /// </summary>
    internal sealed partial class UnixSemaphore : IInterprocessSemaphore
    {
        private const string PathSuffix = ".cloudtoid/interprocess/sem";
        private const string Extension = ".socket";

        private readonly object serverLockObject = new object();
        private readonly SharedAssetsIdentifier identifier;
        private readonly Client client;
        private Server? server;

        internal UnixSemaphore(SharedAssetsIdentifier identifier)
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

        public bool WaitOne(int millisecondsTimeout)
            => client.Wait(millisecondsTimeout);

        public ValueTask ReleaseAsync()
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
