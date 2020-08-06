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
        private Socket socket;

        internal UnixDomainSocketClient(string file)
        {
            endpoint = new UnixDomainSocketEndPoint(file);
            socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
            socket.Blocking = false;
        }

        internal bool IsConnected
            => socket?.Connected ?? false;

        public void Dispose()
        {
            cancellationSource.Cancel();
            socket.SafeDispose();
        }

        internal async ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellation)
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                cancellation);


            await EnsureConnectedAsync(source.Token);

            try
            {
                return await socket.ReceiveAsync(buffer, SocketFlags.None, source.Token);
            }
            catch
            {
                if (!socket.Connected)
                    ResetSocket();

                throw;
            }
        }

        private void ResetSocket()
        {
            socket.SafeDispose();
            socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
            socket.Blocking = false;
        }

        private async Task EnsureConnectedAsync(CancellationToken cancellation)
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
    }
}
