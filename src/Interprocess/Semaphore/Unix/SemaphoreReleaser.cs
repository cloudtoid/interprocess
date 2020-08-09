using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.DomainSocket;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    internal sealed class SemaphoreReleaser : IInterprocessSemaphoreReleaser
    {
        private static readonly byte[] message = new byte[] { 1 };
        private readonly string filePath;
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private Socket?[] clients = Array.Empty<Socket>();

        internal SemaphoreReleaser(SharedAssetsIdentifier identifier)
        {
            filePath = Util.CreateShortUniqueFileName(
                identifier.Path,
                identifier.Name,
                Constants.Extension);

            if (filePath.Length > 104)
                throw new ArgumentException($"The queue path and queue name together are too long for this OS. File: '{filePath}'");

            StartServer();
        }

        // used for testing
        internal int ClientCount
            => clients.Count(c => c != null);

        public void Dispose()
            => cancellationSource.Cancel();

        public async Task ReleaseAsync(CancellationToken cancellation)
        {
            // take a snapshot as the ref to the array may change
            var clients = this.clients;

            var count = clients.Length;
            var tasks = ArrayPool<ValueTask>.Shared.Rent(count);

            try
            {
                for (int i = 0; i < count; i++)
                    tasks[i] = ReleaseAsync(clients, i, cancellation);

                // do not use Task.WaitAll
                for (int i = 0; i < count; i++)
                    await tasks[i];
            }
            finally
            {
                ArrayPool<ValueTask>.Shared.Return(tasks);
            }
        }

        private static async ValueTask ReleaseAsync(
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
            var thread = new Thread(ServerLoop);
            thread.IsBackground = true;
            thread.Start();
        }

        private void ServerLoop()
        {
            var server = new UnixDomainSocketServer(filePath);
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
                        server = new UnixDomainSocketServer(filePath);
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                // if there is an error here, we are in a bad state.
                // treat this as a fatal exception and crash the process
                Environment.FailFast(
                    "Unix domain socket server failed leaving the application in a bad state. " +
                    "The only option is to crash the application.", ex);
            }
            finally
            {
                foreach (var client in clients)
                    client.SafeDispose();

                server.Dispose();
            }
        }
    }
}
