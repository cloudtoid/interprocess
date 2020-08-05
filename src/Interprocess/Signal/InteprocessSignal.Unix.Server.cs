using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Signal.Unix
{
    internal sealed class Server : IDisposable
    {
        private const string PathPrefix = ".cloudtoid/interprocess/signal";
        private const string Extension = ".socket";
        private static readonly byte[] message = new byte[] { 1 };

        private readonly ManualResetEvent stoppedWaitHandle = new ManualResetEvent(false);
        private readonly Socket listener;
        private readonly string file;
        private Socket?[] clients = Array.Empty<Socket>();
        private volatile bool disposed;

        public Server(string queueName, string path)
        {
            (listener, file) = StartServer(queueName, path);
        }

        ~Server()
            => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    // cancel all currently running background activities
                    disposed = true;

                    try
                    {
                        listener.Dispose();
                        stoppedWaitHandle.WaitOne(2000);
                    }
                    finally
                    {
                        foreach (var client in clients)
                        {
                            try
                            {
                                client?.Dispose();
                            }
                            catch { }
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    File.Delete(file);
                }
                catch { }
            }
        }

        internal async ValueTask SignalAsync()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(Server));

            // take a snapshot as the array may change
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
                    catch { }

                    if (!client.Connected)
                    {
                        clients[i] = null;
                        client.Dispose();
                    }
                }
            }
        }

        private (Socket, string File) StartServer(string queueName, string path)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            string file;

            try
            {
                var index = 0;
                while (true)
                {
                    var fileName = queueName;
                    if (index++ != 0)
                        fileName += index.ToString(CultureInfo.InvariantCulture);

                    file = Path.Combine(path, PathPrefix, fileName + Extension);
                    var endpoint = new UnixDomainSocketEndPoint(file);
                    try
                    {
                        socket.Bind(endpoint);
                    }
                    catch (SocketException se) when (se.ErrorCode == 48 || se.Message.Contains("in use", StringComparison.OrdinalIgnoreCase)) // socket in use
                    {
                        continue;
                    }

                    break;
                }

                socket.Listen(100);
                _ = ServerLoopAsync(socket);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            return (socket, file);
        }

        private async Task ServerLoopAsync(Socket server)
        {
            try
            {
                while (!disposed)
                {
                    var client = await server.AcceptAsync();
                    var newClients = new Socket?[] { client };
                    while (true)
                    {
                        var snapshot = clients;

                        if (snapshot != null)
                            newClients = snapshot.Concat(newClients).Where(c => c != null).ToArray();

                        if (Interlocked.CompareExchange(ref clients, newClients, snapshot) == snapshot)
                            break;
                    }
                }
            }
            finally
            {
                stoppedWaitHandle.Set();
            }
        }
    }
}
