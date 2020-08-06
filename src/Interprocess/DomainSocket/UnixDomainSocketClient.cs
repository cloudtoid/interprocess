using System;
using System.Diagnostics;
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
            EnsureSocket();
            Debug.Assert(socket != null);

            using var source = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                cancellation);

            await ConnectAsync(source.Token);
            return await socket.ReceiveAsync(buffer, SocketFlags.None, source.Token);
        }

        private void EnsureSocket()
        {
            if (socket != null && !socket.Connected)
            {
                socket.SafeDispose();
                socket = null;
            }

            if (socket is null)
            {
                socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
                socket.Blocking = false;
            }
        }

        private async Task ConnectAsync(CancellationToken cancellation)
        {
            Debug.Assert(socket != null);
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
