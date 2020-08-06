using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.DomainSocket;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    internal partial class UnixSemaphore
    {
        // internal for testing
        internal sealed class Client : IDisposable
        {
            private static readonly EnumerationOptions enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.PlatformDefault,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,
            };

            private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
            private readonly AutoResetEvent fileWatcherHandle = new AutoResetEvent(false);
            private readonly AutoResetEvent signalWaitHandle = new AutoResetEvent(false);
            private readonly SharedAssetsIdentifier identifier;
            private FileSystemWatcher? watcher;

            internal Client(SharedAssetsIdentifier identifier)
            {
                this.identifier = identifier;
                Task.Run(() => StartClients(cancellationSource.Token));
                StartFileWatcher();
            }

            public void Dispose()
            {
                cancellationSource.Cancel();
                StopFileWatcher();
                signalWaitHandle.Dispose();
                fileWatcherHandle.Dispose();
            }

            internal bool Wait(int millisecondsTimeout)
                => signalWaitHandle.WaitOne(millisecondsTimeout);

            private void StartFileWatcher()
            {
                var path = identifier.Path;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(Environment.CurrentDirectory, path);

                watcher = new FileSystemWatcher(path, identifier.Name + "*" + Extension);
                watcher.Error += OnWatcherError;
                watcher.Created += SocketFileAddedOrDeleted;
                watcher.Deleted += SocketFileAddedOrDeleted;
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
                        snapshot.Created -= SocketFileAddedOrDeleted;
                        snapshot.Deleted -= SocketFileAddedOrDeleted;
                    }
                }
                catch { }
            }

            private void OnWatcherError(object sender, ErrorEventArgs e)
            {
                StopFileWatcher();
                StartFileWatcher();
            }

            private void SocketFileAddedOrDeleted(object sender, FileSystemEventArgs e)
                => fileWatcherHandle.Set();

            private void StartClients(CancellationToken cancellation)
            {
                var fileSearchPattern = identifier.Name + "*" + Extension;
                var clients = new Dictionary<string, UnixDomainSocketClient>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        var files = Directory.GetFiles(identifier.Path, fileSearchPattern, enumerationOptions);

                        // remove disconected or closed clients
                        var toRemove = clients.Where(c =>
                            !c.Value.IsConnected || !files.Contains(c.Key, StringComparer.OrdinalIgnoreCase));

                        foreach (var remove in toRemove)
                        {
                            clients.Remove(remove.Key);
                            remove.Value.Dispose();
                        }

                        // new clients to add
                        foreach (var add in files.Where(f => !clients.ContainsKey(f)))
                        {
                            _ = ReceiveAsync(add, cancellation);
                        }

                        fileWatcherHandle.WaitOne(20);
                    }
                }
                finally
                {
                    foreach (var client in clients)
                        client.Value.Dispose();
                }
            }

            private async ValueTask ReceiveAsync(string file, CancellationToken cancellation)
            {
                var buffer = new byte[1];

                try
                {
                    using var client = new UnixDomainSocketClient(file);
                    while (!cancellation.IsCancellationRequested)
                    {
                        if (await client.ReceiveAsync(buffer, cancellation) > 0)
                            signalWaitHandle.Set();
                    }
                }
                catch { }
            }
        }
    }
}
