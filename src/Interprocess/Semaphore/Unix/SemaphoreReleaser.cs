using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        private static readonly byte[] Message = new byte[] { 1 };
        private readonly CancellationTokenSource stopSource = new CancellationTokenSource();
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

            filePath = PathUtil.CreateShortUniqueFileName(
                identifier.Path,
                identifier.Name,
                Constants.Extension);

            if (filePath.Length > 104)
                throw new ArgumentException($"The queue path and queue name together are too long for this OS. File: '{filePath}'");

            StartServer(out connectionAcceptThread, out releaseLoopThread);
        }

        ~SemaphoreReleaser()
            => stopSource.Cancel();  // release the threads

        // used for testing
        internal int ClientCount
            => clients.WhereNotNull().Count();

        public void Dispose()
        {
            stopSource.Cancel();
            connectionAcceptThread.Join();
            releaseLoopThread.Join();
            GC.SuppressFinalize(this);
        }

        public void Release()
        {
            if (clients.Length > 0)
                releaseSignal.Set();
        }

        [SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "Used in a creation of a thread.")]
        private void StartServer(out Thread connectionAcceptThread, out Thread releaseLoopThread)
        {
            // using dedicated threads as these are long running and looping operations
            connectionAcceptThread = new Thread(ConnectionAcceptLoop);
            connectionAcceptThread.Name = "ConnectionAcceptLoop";
            connectionAcceptThread.IsBackground = true;
            connectionAcceptThread.Start();

            releaseLoopThread = new Thread(() => ReleaseLoopAsync().Wait());
            releaseLoopThread.Name = "ReleaseLoopAsync";
            releaseLoopThread.IsBackground = true;
            releaseLoopThread.Start();
        }

        private void ConnectionAcceptLoop()
        {
            var server = new UnixDomainSocketServer(filePath, loggerFactory);

            try
            {
                Async.LoopTillCancelled(
                    cancellation =>
                    {
                        try
                        {
                            var client = server.Accept(cancellation);
                            clients = clients.WhereNotNull().Concat(client).ToArray();
                        }
                        catch (SocketException se)
                        {
                            logger.LogError(se, "Accepting a Unix Domain Socket connection failed unexpectedly.");
                            server.Dispose();
                            server = new UnixDomainSocketServer(filePath, loggerFactory);
                        }
                    },
                    logger,
                    stopSource.Token);
            }
            finally
            {
                foreach (var client in clients)
                    client.SafeDispose(logger);

                server.Dispose();
            }
        }

        private async Task ReleaseLoopAsync()
        {
            const int MaxClientCount = 1000;
            var tasks = new ValueTask[MaxClientCount];
            using (releaseSignal)
            {
                await Async.LoopTillCancelledAsync(
                    async cancellation =>
                    {
                        if (!releaseSignal.WaitOne(10))
                            return;

                        // take a snapshot as the ref to the array may change
                        var clients = this.clients;

                        var count = Math.Min(clients.Length, MaxClientCount);
                        if (count == 0)
                            return;

                        for (var i = 0; i < count; i++)
                            tasks[i] = ReleaseAsync(clients, i, cancellation);

                        // do not use Task.WaitAll
                        for (var i = 0; i < count; i++)
                            await tasks[i].ConfigureAwait(false);
                    },
                    logger,
                    stopSource.Token)
                    .ConfigureAwait(false);
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
                    .SendAsync(Message, SocketFlags.None, cancellation)
                    .ConfigureAwait(false);

                Debug.Assert(bytesSent == Message.Length, "EExpected the bytesSent to be 1");
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.Shutdown)
            {
                logger.LogDebug("Server has shutdown a connection to this '{0}' Unix Domain Socket server.", filePath);
                clients[i] = null;
                client.SafeDispose(logger);
            }
            catch when (!client.Connected)
            {
                logger.LogError("Client is no longer connected to this '{0}' Unix Domain Socket server.", filePath);
                clients[i] = null;
                client.SafeDispose(logger);
            }
        }
    }
}
