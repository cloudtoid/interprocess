using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal sealed class UnixDomainSocketClient : IDisposable
    {
        private const int ConnectMillisecondTimeout = 100;
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly UnixDomainSocketEndPoint endpoint;
        private Socket? socket;

        internal UnixDomainSocketClient(string file)
        {
            endpoint = new UnixDomainSocketEndPoint(file);
            socket = Util.CreateUnixDomainSocket(blocking: false);
        }

        public void Dispose()
        {
            cancellationSource.Cancel();
            Interlocked.Exchange(ref socket, null).SafeDispose();
        }

        internal async ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();

            using var source = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                cancellation);

            var socket = GetSocket();
            try
            {
                await EnsureConnectedAsync(socket, source.Token);
                return await socket.ReceiveAsync(buffer, SocketFlags.None, source.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Socket receive operation cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Socket receive failed unexpectedly. " + ex.Message);

                if (!socket.Connected)
                    Interlocked.CompareExchange(ref this.socket, null, socket).SafeDispose();

                throw;
            }
        }

        private async Task EnsureConnectedAsync(
            Socket socket,
            CancellationToken cancellation)
        {
            if (socket.Connected)
                return;

            var startTime = DateTime.Now;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();

                try
                {
                    socket.Connect(endpoint);
                    return;
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.WouldBlock)
                {
                    var duration = (DateTime.Now - startTime).Milliseconds;
                    if (duration > ConnectMillisecondTimeout)
                        throw new TimeoutException("Socket.Connect timeout expired.");

                    await Task.Delay(5, cancellation);
                }
            }
        }

        private Socket GetSocket()
        {
            while (true)
            {
                var snapshot = socket;
                if (snapshot != null)
                    return snapshot;

                var newSocket = Util.CreateUnixDomainSocket(blocking: false);
                if (Interlocked.CompareExchange(ref socket, newSocket, null) == null)
                    return newSocket;
            }
        }
    }
}
