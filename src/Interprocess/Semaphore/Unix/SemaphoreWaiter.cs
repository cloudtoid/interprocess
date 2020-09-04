using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    internal sealed partial class SemaphoreWaiter : IInterprocessSemaphoreWaiter
    {
        private static readonly byte[] MessageBuffer = new byte[1];
        private static readonly EnumerationOptions EnumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.PlatformDefault,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        private readonly CancellationTokenSource stopSource = new CancellationTokenSource();
        private readonly AutoResetEvent fileWatcherHandle = new AutoResetEvent(false);
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(0, int.MaxValue);
        private readonly Action releaseDelegate;
        private readonly Thread clientsLoopThread;
        private readonly SharedAssetsIdentifier identifier;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger<SemaphoreWaiter> logger;

        [SuppressMessage("CodeQuality", "IDE0069:Disposable fields should be disposed", Justification = "It is disposed in StopFileWatcher")]
        private FileSystemWatcher? watcher;

        internal SemaphoreWaiter(
            SharedAssetsIdentifier identifier,
            ILoggerFactory loggerFactory)
        {
            this.identifier = identifier;
            this.loggerFactory = loggerFactory;
            logger = loggerFactory.CreateLogger<SemaphoreWaiter>();
            releaseDelegate = () => semaphore.Release();

            clientsLoopThread = StartRceivers();
            StartFileWatcher();
        }

        ~SemaphoreWaiter()
           => stopSource.Cancel(); // release the threads

        public void Dispose()
        {
            StopFileWatcher();
            stopSource.Cancel();
            clientsLoopThread.Join();
            semaphore.Dispose();
            fileWatcherHandle.Dispose();
            stopSource.Dispose();
            GC.SuppressFinalize(this);
        }

        public bool Wait(int millisecondsTimeout)
            => semaphore.Wait(millisecondsTimeout);

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
            catch (Exception ex) when (!ex.IsFatal())
            {
                logger.LogError(ex, "Failed to fully dispose an old file watcher.");
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            if (stopSource.Token.IsCancellationRequested)
                return;

            StopFileWatcher();
            StartFileWatcher();
        }

        private void OnSocketFileAddedOrDeleted(object sender, FileSystemEventArgs e)
            => fileWatcherHandle.Set();

        private Thread StartRceivers()
        {
            // using a dedicated thread as this is a very long blocking call
            var thread = new Thread(ReceiversLoop)
            {
                Name = "ReceiversLoop",
                IsBackground = true
            };
            thread.Start();
            return thread;
        }

        private void ReceiversLoop()
        {
            var fileSearchPattern = identifier.Name + "*" + Constants.Extension;
            var receivers = new Dictionary<string, Receiver>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Async.LoopTillCancelled(
                    cancellation =>
                    {
                        var files = Directory.GetFiles(identifier.Path, fileSearchPattern, EnumerationOptions);

                        // remove closed clients
                        var toRemove = receivers.Where(r => !files.Contains(r.Key, StringComparer.OrdinalIgnoreCase));

                        foreach (var remove in toRemove)
                        {
                            receivers.Remove(remove.Key);
                            remove.Value.Dispose();
                            logger.LogDebug(
                                "The Unix Domain Socket server on '{0}' is no longer available and the receiver for it is now removed.",
                                remove.Key);
                        }

                        // new clients to add
                        foreach (var add in files.Where(file => !receivers.ContainsKey(file)))
                        {
                            var receiver = new Receiver(add, releaseDelegate, loggerFactory);
                            receivers.Add(add, receiver);
                            logger.LogDebug(
                                "A Unix Domain Socket server for '{0}' is discovered and a receiver is created for it.",
                                add);
                        }

                        fileWatcherHandle.WaitOne(100);
                    },
                    logger,
                    stopSource.Token);
            }
            finally
            {
                foreach (var client in receivers)
                    client.Value.Dispose();
            }
        }
    }
}
