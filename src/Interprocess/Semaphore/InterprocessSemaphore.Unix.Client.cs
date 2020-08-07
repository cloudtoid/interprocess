using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.DomainSocket;
using SysSemaphoree = System.Threading.Semaphore;

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
            private readonly SysSemaphoree semaphore = new SysSemaphoree(0, int.MaxValue);
            private readonly ManualResetEvent stoppedWaitHandle = new ManualResetEvent(false);
            private readonly SharedAssetsIdentifier identifier;
            private FileSystemWatcher? watcher;

            internal Client(SharedAssetsIdentifier identifier)
            {
                this.identifier = identifier;
                StartClients();
                StartFileWatcher();
            }

            public void Dispose()
            {
                StopFileWatcher();
                cancellationSource.Cancel();
                stoppedWaitHandle.WaitOne();
                stoppedWaitHandle.Dispose();
                semaphore.Dispose();
                fileWatcherHandle.Dispose();
            }

            internal bool Wait(int millisecondsTimeout)
                => semaphore.WaitOne(millisecondsTimeout);

            private void StartFileWatcher()
            {
                watcher = new FileSystemWatcher(identifier.Path, identifier.Name + "*" + Extension);
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

            private void OnSocketFileAddedOrDeleted(object sender, FileSystemEventArgs e)
                => fileWatcherHandle.Set();

            private void StartClients()
            {
                // using a dedicated thread as this is a very long blocking call
                var thread = new Thread(StartClientsCore);
                thread.IsBackground = true;
                thread.Start();
            }

            private void StartClientsCore()
            {
                var cancellation = cancellationSource.Token;
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
                catch when (cancellation.IsCancellationRequested) { }
                catch
                {
                    // if there is an error here, we are in a bad state.
                    // treat this as a fatal exception and crash the process
                    Environment.FailFast("Unix domain socket client failed leaving the application in a bad state.");
                }
                finally
                {
                    foreach (var client in clients)
                        client.Value.Dispose();

                    stoppedWaitHandle.Set();
                }
            }

            private async Task ReceiveAsync(
                string file,
                UnixDomainSocketClient client,
                CancellationToken cancellation)
            {
                var buffer = new byte[1];

                try
                {
                    using (client)
                    {
                        try
                        {
                            while (!cancellation.IsCancellationRequested)
                            {
                                if (await client.ReceiveAsync(buffer, cancellation) == 0)
                                {
                                    Console.WriteLine("Looks like the server is shutting down.");
                                    return;
                                }

                                semaphore.Release();
                            }
                        }
                        catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionRefused)
                        {
                            Console.WriteLine("Found an orphaned semaphore lock file");
                            Util.TryDeleteFile(file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Receive loop stopped - " + ex.Message);
                }
            }
        }
    }
}
