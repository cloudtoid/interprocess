using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.DomainSocket;
using Microsoft.Extensions.Logging;
using SysSemaphoree = System.Threading.Semaphore;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    internal sealed class SemaphoreWaiter : IInterprocessSemaphoreWaiter
    {
        private static readonly byte[] messageBuffer = new byte[1];
        private static readonly EnumerationOptions enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.PlatformDefault,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly AutoResetEvent fileWatcherHandle = new AutoResetEvent(false);
        private readonly SysSemaphoree semaphore = new SysSemaphoree(0, int.MaxValue);
        private readonly Thread clientsLoopThread;
        private readonly SharedAssetsIdentifier identifier;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger<SemaphoreWaiter> logger;
        private FileSystemWatcher? watcher;

        internal SemaphoreWaiter(
            SharedAssetsIdentifier identifier,
            ILoggerFactory loggerFactory)
        {
            this.identifier = identifier;
            this.loggerFactory = loggerFactory;
            logger = loggerFactory.CreateLogger<SemaphoreWaiter>();

            clientsLoopThread = StartClients();
            StartFileWatcher();
        }

        public void Dispose()
        {
            logger.LogInformation("Disposing " + nameof(SemaphoreWaiter));
            StopFileWatcher();
            cancellationSource.Cancel();
            clientsLoopThread.Join();
            semaphore.Dispose();
            fileWatcherHandle.Dispose();
        }

        public bool WaitOne(int millisecondsTimeout)
            => semaphore.WaitOne(millisecondsTimeout);

        private void StartFileWatcher()
        {
            watcher = new FileSystemWatcher(identifier.Path, identifier.Name + "*" + Constants.Extension);
            watcher.Error += OnWatcherError;
            watcher.Created += OnSocketFileAddedOrDeleted;
            watcher.Deleted += OnSocketFileAddedOrDeleted;
            watcher.EnableRaisingEvents = true;
        }

        private void StopFileWatcher()
        {
            var snapshot = Interlocked.Exchange(ref watcher, null);
            if (snapshot is null)
                return;

            try
            {
                using (snapshot)
                {
                    snapshot.EnableRaisingEvents = false;
                    snapshot.Error -= OnWatcherError;
                    snapshot.Created -= OnSocketFileAddedOrDeleted;
                    snapshot.Deleted -= OnSocketFileAddedOrDeleted;
                }
            }
            catch
            {
                logger.LogError("Failed to fully dispose an old file watcher.");
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            if (cancellationSource.Token.IsCancellationRequested)
                return;

            StopFileWatcher();
            StartFileWatcher();
        }

        private void OnSocketFileAddedOrDeleted(object sender, FileSystemEventArgs e)
            => fileWatcherHandle.Set();

        private Thread StartClients()
        {
            // using a dedicated thread as this is a very long blocking call
            var thread = new Thread(ClientsLoop);
            thread.IsBackground = true;
            thread.Start();
            return thread;
        }

        private void ClientsLoop()
        {
            var cancellation = cancellationSource.Token;
            var fileSearchPattern = identifier.Name + "*" + Constants.Extension;
            var clients = new Dictionary<string, UnixDomainSocketClient>(StringComparer.OrdinalIgnoreCase);
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var files = Directory.GetFiles(identifier.Path, fileSearchPattern, enumerationOptions);

                    // remove closed clients
                    var toRemove = clients.Where(c => !files.Contains(c.Key, StringComparer.OrdinalIgnoreCase));

                    foreach (var remove in toRemove)
                    {
                        clients.Remove(remove.Key);
                        remove.Value.Dispose();
                        logger.LogInformation(
                            $"The Unix Domain Socket server on '{remove}' is no longer available and the client for it is now removed.");
                    }

                    // new clients to add
                    foreach (var add in files.Where(f => !clients.ContainsKey(f)))
                    {
                        var client = new UnixDomainSocketClient(add, loggerFactory);
                        clients.Add(add, client);
                        logger.LogInformation($"A Unix Domain Socket server for '{add}' is discovered and a client is created for it.");
                        Task.Run(() => ReceiveAsync(client, cancellation), cancellation);
                    }

                    fileWatcherHandle.WaitOne(100);
                }
            }
            catch when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                // if there is an error here, we are in a bad state.
                // treat this as a fatal exception and crash the process
                logger.FailFast(
                    "Unix domain socket client failed leaving the application in a bad state. " +
                    "The only option is to crash the application.", ex);
            }
            finally
            {
                foreach (var client in clients)
                    client.Value.Dispose();
            }
        }

        private async Task ReceiveAsync(
            UnixDomainSocketClient client,
            CancellationToken cancellation)
        {
            try
            {
                using (client)
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        if (await client.ReceiveAsync(messageBuffer, cancellation).ConfigureAwait(false) == 0)
                        {
                            logger.LogDebug("Looks like the server is shutting down.");
                            return;
                        }

                        semaphore.Release();
                    }
                }
            }
            catch
            {
            }
        }
    }
}
