using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Signal.Unix
{
    internal sealed class Server : IDisposable
    {
        private const string PathPrefix = ".cloudtoid/interprocess/signal";
        private const string Extension = ".socket";
        private static readonly byte[] message = new byte[] { 1 };

        private Socket? listener;
        private Socket?[] clients = Array.Empty<Socket>();
        private volatile bool disposed;

        public Server(string queueName, string path)
        {
            _ = StartServerAsync(queueName, path);
        }

        ~Server()
            => DisposeInternal();

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
            listener?.Dispose(); // stops the call to listener.AcceptAsync
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

        private async Task StartServerAsync(string queueName, string path)
        {
            while (!disposed)
            {
                try
                {
                    await ListenAsync(queueName, path);
                }
                catch (SocketException) { }
            }
        }

        private async Task ListenAsync(string queueName, string path)
        {
            string? file = null;
            try
            {
                using (listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                {
                    while (!disposed)
                    {
                        file = GetRandomEndPointFile(queueName, path);
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
                {
                    try
                    {
                        client?.Dispose();
                    }
                    catch { }
                }
                clients = Array.Empty<Socket>();
            }
        }

        private static string GetRandomEndPointFile(string queueName, string path)
        {
            var index = (int)(Math.Abs(DateTime.Now.Ticks - DateTime.Today.Ticks) % 100000);
            var fileName = queueName + index.ToString(CultureInfo.InvariantCulture) + Extension;
            return Path.Combine(path, PathPrefix, fileName);
        }

        private static bool IsSocketInUse(SocketException se)
            => se.ErrorCode == 48 || se.Message.Contains("in use", StringComparison.OrdinalIgnoreCase);
    }
}
