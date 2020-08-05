using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    internal partial class UnixSemaphore
    {
        // internal for testing
        internal sealed class Server : IDisposable
        {
            private static readonly byte[] message = new byte[] { 1 };
            private readonly SharedAssetsIdentifier identifier;
            private Socket? listener;
            private Socket?[] clients = Array.Empty<Socket>();
            private volatile bool disposed;

            internal Server(SharedAssetsIdentifier identifier)
            {
                this.identifier = identifier;
                _ = Task.Run(StartServerAsync);
            }

            ~Server() => DisposeInternal(); // this is important to delete the socket file

            public void Dispose()
            {
                DisposeInternal();
                GC.SuppressFinalize(this);
            }

            private void DisposeInternal()
            {
                if (disposed)
                    return;

                disposed = true; // stops the listening loop
                listener?.Dispose(); // stops/cancels the call to listener.AcceptAsync
            }

            internal async ValueTask SignalAsync()
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(Server));

                // take a snapshot as the ref to the array may change
                var clients = this.clients;

                var count = clients.Length;
                for (int i = 0; i < count; i++)
                {
                    var client = clients[i];
                    if (client != null)
                    {
                        try
                        {
                            await client.SendAsync(message, SocketFlags.None);
                        }
                        catch
                        {
                            if (!client.Connected)
                            {
                                clients[i] = null;
                                client.Dispose();
                            }
                        }
                    }
                }
            }

            private async Task StartServerAsync()
            {
                while (!disposed)
                {
                    try
                    {
                        await ListenAsync();
                    }
                    catch (SocketException) { }
                }
            }

            private async Task ListenAsync()
            {
                string? file = null;
                try
                {
                    using (listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                    {
                        while (!disposed)
                        {
                            file = GetRandomEndPointFile();
                            try
                            {
                                listener.Bind(new UnixDomainSocketEndPoint(file));
                            }
                            catch (SocketException se) when (IsSocketInUse(se)) // socket in use
                            {
                                continue;
                            }

                            break;
                        }

                        listener.Listen(100);
                        await AcceptConnectionsAsync(listener);
                    }
                }
                finally
                {
                    try
                    {
                        if (file != null)
                            File.Delete(file);
                    }
                    catch { }
                }
            }

            private async Task AcceptConnectionsAsync(Socket listener)
            {
                try
                {
                    while (!disposed)
                    {
                        var client = await listener.AcceptAsync();
                        clients = clients.Concat(new[] { client }).Where(c => c != null).ToArray();
                    }
                }
                finally
                {
                    foreach (var client in clients)
                            client.SafeDispose();

                    clients = Array.Empty<Socket>();
                }
            }

            private string GetRandomEndPointFile()
            {
                var index = (int)(Math.Abs(DateTime.Now.Ticks - DateTime.Today.Ticks) % 100000);
                var fileName = identifier.Name + index.ToString(CultureInfo.InvariantCulture) + Extension;
                return Path.Combine(identifier.Path, fileName);
            }

            private static bool IsSocketInUse(SocketException se)
                => se.ErrorCode == 48 || se.Message.Contains("in use", StringComparison.OrdinalIgnoreCase);
        }
    }
}
