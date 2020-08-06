using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal sealed class UnixDomainSocketServer : IDisposable
    {
        private readonly string file;
        private Socket? socket;
        private CancellationTokenSource cancellationSource = new CancellationTokenSource();

        internal UnixDomainSocketServer(string file)
        {
            this.file = file;
        }

        ~UnixDomainSocketServer()
            => CleanUp();

        public void Dispose()
        {
            CleanUp();
            GC.SuppressFinalize(this);
        }

        internal Socket Accept(CancellationToken cancellation)
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                cancellation);

            if (socket is null)
            {
                socket = UnixDmainSocketUtil.CreateUnixDomainSocket();
                socket.Bind(new UnixDomainSocketEndPoint(file));
                socket.Listen(100);
            }

            try
            {
                return UnixDmainSocketUtil.SocketOperation(
                    callback => socket.BeginAccept(callback, null),
                    token => socket.EndAccept(token),
                    source.Token);
            }
            catch (OperationCanceledException)
            {
                CleanUp();
                cancellationSource = new CancellationTokenSource();
                throw;
            }
        }

        private void CleanUp()
        {
            cancellationSource.Cancel();
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
