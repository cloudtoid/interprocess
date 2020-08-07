using System;
using System.Buffers;
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
                StartServer();
            }

            // used for testing
            internal int ClientCount
                => clients.Count(c => c != null);

            public void Dispose()
                => cancellationSource.Cancel();

            internal async Task SignalAsync(CancellationToken cancellation)
            {
                // take a snapshot as the ref to the array may change
                var clients = this.clients;

                var count = clients.Length;
                var tasks = ArrayPool<ValueTask>.Shared.Rent(count);

                try
                {
                    for (int i = 0; i < count; i++)
                        tasks[i] = SignalAsync(clients, i, cancellation);

                    for (int i = 0; i < count; i++)
                        await tasks[i];
                }
                finally
                {
                    ArrayPool<ValueTask>.Shared.Return(tasks);
                }
            }

            private static async ValueTask SignalAsync(
                Socket?[] clients,
                int i,
                CancellationToken cancellation)
            {
                var client = clients[i];
                if (client == null)
                    return;

                try
                {
                    var bytesSent = await client.SendAsync(
                        message,
                        SocketFlags.None,
                        cancellation);

                    Debug.Assert(bytesSent == message.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to send a signal. " + ex.Message);

                    if (!client.Connected)
                    {
                        clients[i] = null;
                        client.SafeDispose();
                    }
                }
            }

            private void StartServer()
            {
                // using a dedicated thread as this is a very long blocking call
                var thread = new Thread(StartServerCore);
                thread.IsBackground = true;
                thread.Start();
            }

            private void StartServerCore()
            {
                var server = CreateServer();
                var cancellation = cancellationSource.Token;

                try
                {
                    while (!cancellation.IsCancellationRequested)
                    {
                        try
                        {
                            var client = server.Accept(cancellation);
                            clients = clients.Concat(new[] { client }).Where(c => c != null).ToArray();
                        }
                        catch (SocketException)
                        {
                            Console.WriteLine("Socket accept failed unexpectedly");

                            server.Dispose();
                            server = CreateServer();
                        }
                    }
                }
                catch when (cancellation.IsCancellationRequested) { }
                catch
                {
                    // if there is an error here, we are in a bad state.
                    // treat this as a fatal exception and crash the process
                    Environment.FailFast("Unix domain socket server failed leaving the application in a bad state.");
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
