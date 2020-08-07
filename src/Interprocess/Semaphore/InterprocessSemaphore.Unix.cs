using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    /// <summary>
    /// .NET Core 3.1  and .NET 5 do not have support for named semaphores on
    /// Unix type OSs (Linux, macOS, etc.). To replicate a named semaphore in
    /// the most efficient possible way, we are using Unix Named Sockets to
    /// send signals between processes.
    /// 
    /// It is worth mentioning that we support multiple signal publishers and
    /// receivers; therefore, you will find some logic to utilize multiple named
    /// sockets. We also use a file system watcher to keep track of the addition
    /// and removal of signal publishers (unix named sockets use backing files).
    ///
    /// This whole class, as well as <see cref="Windows.WindowsSemaphore"/> should
    /// be removed and replaced with <see cref="System.Threading.Semaphore"/> once
    /// named semaphores are supported on all platforms.
    /// </summary>
    internal sealed partial class UnixSemaphore : IInterprocessSemaphore
    {
        private const string PathSuffix = ".cloudtoid/interprocess/sem";
        private const string Extension = ".soc";

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

        public Task ReleaseAsync(CancellationToken cancellation)
            => EnsureServer().SignalAsync(cancellation);

        private Server EnsureServer()
        {
            if (server is null)
            {
                lock (serverLockObject)
                {
                    if (server is null)
                        server = new Server(identifier);
                }
            }

            return server;
        }
    }
}
