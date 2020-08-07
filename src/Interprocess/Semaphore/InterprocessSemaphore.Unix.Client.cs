using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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
            private readonly ManualResetEvent stoppedWaitHandle = new ManualResetEvent(false);
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
                StopFileWatcher();
                cancellationSource.Cancel();
                stoppedWaitHandle.WaitOne();
                stoppedWaitHandle.Dispose();
                signalWaitHandle.Dispose();
                fileWatcherHandle.Dispose();
            }

            internal bool Wait(int millisecondsTimeout)
                => signalWaitHandle.WaitOne(millisecondsTimeout);

            private void StartFileWatcher()
            {
                watcher = new FileSystemWatcher(identifier.Path, identifier.Name + "*" + Extension);
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
                catch
                {
                    Console.WriteLine("Failed to fully dispose an old file watcher.");
                }
            }

            private void OnWatcherError(object sender, ErrorEventArgs e)
            {
                if (cancellationSource.Token.IsCancellationRequested)
                    return;

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

                        // remove closed clients
                        var toRemove = clients.Where(c => !files.Contains(c.Key, StringComparer.OrdinalIgnoreCase));

                        foreach (var remove in toRemove)
                        {
                            clients.Remove(remove.Key);
                            remove.Value.Dispose();

                            Console.WriteLine("removed a client: " + remove.Key);
                        }

                        // new clients to add
                        foreach (var add in files.Where(f => !clients.ContainsKey(f)))
                        {
                            var client = new UnixDomainSocketClient(add);
                            clients.Add(add, client);
                            Console.WriteLine("Added a client: " + add);
                            Task.Run(() => ReceiveAsync(add, client, cancellation), cancellation);
                        }

                        fileWatcherHandle.WaitOne(100);
                    }
                }
                finally
                {
                    foreach (var client in clients)
                        client.Value.Dispose();

                    stoppedWaitHandle.Set();
                }
            }

            private async ValueTask ReceiveAsync(
                string file,
                UnixDomainSocketClient client,
                CancellationToken cancellation)
            {
                var buffer = new byte[1];
                using (client)
                {
                    try
                    {
                        while (!cancellation.IsCancellationRequested)
                        {
                            var count = await client.ReceiveAsync(buffer, cancellation);
                            if (count == 1)
                                signalWaitHandle.Set();
                            else
                                Console.WriteLine($"Received {count} bytes that is unexpected");
                        }
                    }
                    catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionRefused)
                    {
                        Console.WriteLine("Found an orphaned semaphore lock file");
                        Util.TryDeleteFile(file);
                    }
                    catch
                    {
                        Console.WriteLine("Receive loop stopped");
                    }
                }
            }
        }
    }
}
