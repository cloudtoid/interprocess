using System;
using System.Diagnostics;
using System.Globalization;
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
        internal sealed class Server : IDisposable
        {
            private static readonly byte[] message = new byte[] { 1 };
            private readonly SharedAssetsIdentifier identifier;
            private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
            private Socket?[] clients = Array.Empty<Socket>();

            internal Server(SharedAssetsIdentifier identifier)
            {
                this.identifier = identifier;
                Task.Run(() => AcceptConnectionsAsync(cancellationSource.Token));
            }

            public void Dispose()
                => cancellationSource.Cancel();

            internal async Task SignalAsync(CancellationToken cancellation)
            {
                // take a snapshot as the ref to the array may change
                var clients = this.clients;

                var count = clients.Length;
                for (int i = 0; i < count; i++)
                {
                    var client = clients[i];
                    if (client == null)
                        continue;

                    try
                    {
                        var bytesSent = await client.SendAsync(
                            message,
                            SocketFlags.None,
                            cancellation);

                        Debug.Assert(bytesSent == message.Length);
                    }
                    catch
                    {
                        if (!client.Connected)
                        {
                            clients[i] = null;
                            client.SafeDispose();
                        }
                    }
                }
            }

            private async Task AcceptConnectionsAsync(CancellationToken cancellation)
            {
                var server = CreateServer();

                try
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        try
                        {
                            var client = await server.AcceptAsync(cancellation);
                            clients = clients.Concat(new[] { client }).Where(c => c != null).ToArray();
                        }
                        catch (SocketException)
                        {
                            server.Dispose();
                            server = CreateServer();
                        }
                    }
                }
                finally
                {
                    foreach (var client in clients)
                        client.SafeDispose();

                    server.Dispose();
                }
            }

            private UnixDomainSocketServer CreateServer()
            {
                var index = (int)(Math.Abs(DateTime.Now.Ticks - DateTime.Today.Ticks) % 100000);
                var fileName = identifier.Name + index.ToString(CultureInfo.InvariantCulture) + Extension;
                var filePath = Path.Combine(identifier.Path, fileName);
                return new UnixDomainSocketServer(filePath);
            }
        }
    }
}
