using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal sealed class UnixDomainSocketClient : IDisposable
    {
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly UnixDomainSocketEndPoint endpoint;
        private Socket? socket;

        internal UnixDomainSocketClient(string file)
        {
            endpoint = new UnixDomainSocketEndPoint(file);
        }

        internal bool IsConnected
            => socket?.Connected ?? false;

        internal async ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellation)
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                cancellation);

            try
            {
                EnsureSocket();
                Connect(source.Token);
                return await ReceiveCoreAsync(buffer, source.Token);
            }
            catch (OperationCanceledException)
            {
                socket.SafeDispose();
                socket = null;
                throw;
            }
        }

        private void EnsureSocket()
        {
            if (socket is null)
                socket = UnixDomainSocketUtil.CreateUnixDomainSocket();

            Debug.Assert(socket.Connected);
        }

        private void Connect(CancellationToken cancellationToken)
        {
            Debug.Assert(socket != null);

            UnixDomainSocketUtil.SocketOperation(
                callback => socket.BeginConnect(endpoint, callback, null),
                token =>
                {
                    socket.EndConnect(token);
                    return true;
                },
                cancellationToken);
        }

        private async ValueTask<int> ReceiveCoreAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            Debug.Assert(socket != null);
            return await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
        }

        public void Dispose()
        { 
            cancellationSource.Cancel();
            socket.SafeDispose();
        }
    }
}
