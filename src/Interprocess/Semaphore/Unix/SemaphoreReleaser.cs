using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.DomainSocket;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    internal sealed class SemaphoreReleaser : IInterprocessSemaphoreReleaser
    {
        private static readonly byte[] message = new byte[] { 1 };
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly AutoResetEvent releaseSignal = new AutoResetEvent(false);
        private readonly Thread releaseLoopThread;
        private readonly Thread connectionAcceptThread;
        private readonly string filePath;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger<SemaphoreReleaser> logger;
        private Socket?[] clients = Array.Empty<Socket>();

        internal SemaphoreReleaser(SharedAssetsIdentifier identifier, ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
            logger = loggerFactory.CreateLogger<SemaphoreReleaser>();

            filePath = Util.CreateShortUniqueFileName(
                identifier.Path,
                identifier.Name,
                Constants.Extension);

            if (filePath.Length > 104)
                throw new ArgumentException($"The queue path and queue name together are too long for this OS. File: '{filePath}'");

            StartServer(out connectionAcceptThread, out releaseLoopThread);
        }

        // used for testing
        internal int ClientCount
            => clients.Count(c => c != null);

        public void Dispose()
        {
            logger.LogInformation("Disposing " + nameof(SemaphoreReleaser));
            cancellationSource.Cancel();
            connectionAcceptThread.Join();
            releaseLoopThread.Join();
        }

        public void Release()
        {
            if (clients.Length > 0)
                releaseSignal.Set();
        }

        private void StartServer(out Thread connectionAcceptThread, out Thread releaseLoopThread)
        {
            // using dedicated threads as these are long running and looping operations
            connectionAcceptThread = new Thread(ConnectionAcceptLoop);
            connectionAcceptThread.IsBackground = true;
            connectionAcceptThread.Start();

            releaseLoopThread = new Thread(async () => await ReleaseLoop());
            releaseLoopThread.IsBackground = true;
            releaseLoopThread.Start();
        }

        private void ConnectionAcceptLoop()
        {
            var cancellation = cancellationSource.Token;
            UnixDomainSocketServer? server = null;

            try
            {
                server = new UnixDomainSocketServer(filePath, loggerFactory);
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        var client = server.Accept(cancellation);
                        clients = clients.Concat(new[] { client }).Where(c => c != null).ToArray();
                    }
                    catch (SocketException se)
                    {
                        logger.LogError(se, "Accepting a Unix Domain Socket connection failed unexpectedly.");
                        server.Dispose();
                        server = new UnixDomainSocketServer(filePath, loggerFactory);
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                // if there is an error here, we are in a bad state.
                // treat this as a fatal exception and crash the process
                logger.FailFast(
                    "Unix semaphore releaser failed leaving the application in a bad state. " +
                    "The only option is to crash the application.", ex);
            }
            finally
            {
                foreach (var client in clients)
                    client.SafeDispose();

                server?.Dispose();
            }
        }

        private async Task ReleaseLoop()
        {
            const int MaxClientCount = 1000;
            var cancellation = cancellationSource.Token;
            var tasks = new ValueTask[MaxClientCount];

            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (!releaseSignal.WaitOne(10))
                        continue;

                    // take a snapshot as the ref to the array may change
                    var clients = this.clients;

                    var count = Math.Min(clients.Length, MaxClientCount);
                    if (count == 0)
                        continue;

                    for (int i = 0; i < count; i++)
                        tasks[i] = ReleaseAsync(clients, i, cancellation);

                    // do not use Task.WaitAll
                    for (int i = 0; i < count; i++)
                        await tasks[i].ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // if there is an error here, we are in a bad state.
                // treat this as a fatal exception and crash the process
                logger.FailFast(
                    "Unix semaphore releaser failed leaving the application in a bad state. " +
                    "The only option is to crash the application.", ex);
            }
            finally
            {
                releaseSignal.Dispose();
            }
        }

        private async ValueTask ReleaseAsync(
            Socket?[] clients,
            int i,
            CancellationToken cancellation)
        {
            var client = clients[i];
            if (client == null)
                return;

            try
            {
                var bytesSent = await client
                    .SendAsync(message, SocketFlags.None, cancellation)
                    .ConfigureAwait(false);

                Debug.Assert(bytesSent == message.Length);
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.Shutdown)
            {
                logger.LogInformation($"Server has shutdown a connection to this '{filePath}' Unix Domain Socket server.");
                clients[i] = null;
                client.SafeDispose();
            }
            catch when (!client.Connected)
            {
                logger.LogError($"Client is no longer connected to this '{filePath}' Unix Domain Socket server.");
                clients[i] = null;
                client.SafeDispose();
            }
        }
    }
}
