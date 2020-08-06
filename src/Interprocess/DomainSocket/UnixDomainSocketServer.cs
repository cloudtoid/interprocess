using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal sealed class UnixDomainSocketServer : IDisposable
    {
        private readonly object cleanupLock = new object();
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly string file;
        private Socket? socket;

        internal UnixDomainSocketServer(string file)
        {
            this.file = file;
        }

        ~UnixDomainSocketServer()
            => CleanUp();

        public void Dispose()
        {
            cancellationSource.Cancel();
            CleanUp();
            GC.SuppressFinalize(this);
        }

        internal async Task<Socket> AcceptAsync(CancellationToken cancellation)
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                cancellation);

            if (socket is null)
            {
                socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
                socket.Blocking = false;
                socket.Bind(new UnixDomainSocketEndPoint(file));
                socket.Listen(100);
            }

            while (!source.Token.IsCancellationRequested)
            {
                try
                {
                    return socket.Accept();
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.WouldBlock)
                {
                    await Task.Delay(10);
                }
            }

            CleanUp();
            throw new OperationCanceledException();
        }

        private void CleanUp()
        {
            lock (cleanupLock)
            {
                socket.SafeDispose();
                socket = null;

                try
                {
                    File.Delete(file);
                }
                catch { }
            }
        }
    }
}
