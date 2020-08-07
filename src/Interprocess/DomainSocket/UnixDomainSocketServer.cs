using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal sealed class UnixDomainSocketServer : IDisposable
    {
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly string file;
        private readonly int connectionQueueSize;
        private Socket? socket;

        internal UnixDomainSocketServer(string file, int connectionQueueSize = 100)
        {
            this.file = file;
            this.connectionQueueSize = connectionQueueSize;
        }

        ~UnixDomainSocketServer()
            => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        internal async Task<Socket> AcceptAsync(CancellationToken cancellation)
        {
            EnsureSocket();
            Debug.Assert(socket != null);

            using var source = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                cancellation);

            while (true)
            {
                source.Token.ThrowIfCancellationRequested();

                try
                {
                    return socket.Accept();
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.WouldBlock)
                {
                    await Task.Delay(10, cancellation);
                }
            }
        }

        private void EnsureSocket()
        {
            if (socket is null)
            {
                socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
                socket.Blocking = false;
                socket.Bind(new UnixDomainSocketEndPoint(file));
                socket.Listen(connectionQueueSize);
            }
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancellationSource.Cancel();
                socket.SafeDispose();
                socket = null;
            }

            try
            {
                File.Delete(file);
            }
            catch { }
        }
    }
}
