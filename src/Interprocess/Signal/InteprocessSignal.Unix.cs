using System.IO;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Signal.Unix
{
    // internal for testing
    internal sealed partial class UnixSignal : IInteprocessSignal
    {
        private readonly string queueName;
        private readonly string path;
        private readonly object serverLockObject = new object();
        private Server? server;

        internal UnixSignal(string queueName, string path)
        {
            this.queueName = queueName;
            this.path = path;
        }

        public void Dispose()
            => server?.Dispose();

        public bool Wait(int millisecondsTimeout)
        {
            return false;
        }

        public ValueTask SignalAsync()
        {
            if (server is null)
            {
                lock (serverLockObject)
                {
                    if (server is null)
                        server = new Server(queueName, path);
                }
            }

            return server.SignalAsync();
        }
    }
}
