using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Signal.Unix
{
    internal partial class UnixSignal
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

            private readonly AutoResetEvent fileWatcherHandle = new AutoResetEvent(false);
            private readonly AutoResetEvent signalWaitHandle = new AutoResetEvent(false);
            private readonly SharedAssetsIdentifier identifier;
            private FileSystemWatcher? watcher;
            private volatile bool disposed;

            internal Client(SharedAssetsIdentifier identifier)
            {
                this.identifier = identifier;
                Task.Run(StartClientsAsync);
                StartFileWatcher();
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                StopFileWatcher();
                signalWaitHandle.Dispose();
                fileWatcherHandle.Dispose();
            }

            internal bool Wait(int millisecondsTimeout)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(Client));

                return signalWaitHandle.WaitOne(millisecondsTimeout);
            }

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

            private async Task StartClientsAsync()
            {
                var fileSearchPattern = identifier.Name + "*" + Extension;
                var clients = new Dictionary<string, Socket>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    while (!disposed)
                    {
                        var files = Directory.GetFiles(identifier.Path, fileSearchPattern, enumerationOptions);

                        // remove disconected or closed/removed clients
                        var toRemove = clients.Where(c => !c.Value.Connected || !files.Contains(c.Key, StringComparer.OrdinalIgnoreCase));
                        foreach (var remove in toRemove)
                        {
                            clients.Remove(remove.Key);
                            remove.Value.SafeDispose();
                        }

                        // new clients to add
                        foreach (var add in files.Where(f => !clients.ContainsKey(f)))
                        {
                            var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                            try
                            {
                                await client.ConnectAsync(new UnixDomainSocketEndPoint(add));
                                clients.Add(add, client);
                            }
                            catch (SocketException)
                            {
                                client.SafeDispose();
                            }
                        }

                        fileWatcherHandle.WaitOne(1000);
                    }
                }
                finally
                {
                    foreach (var client in clients)
                        client.Value.SafeDispose();
                }
            }
        }
    }
}
