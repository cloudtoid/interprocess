using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

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

        internal Socket Accept(CancellationToken cancellation)
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
                    Thread.Sleep(5);
                }
            }
        }

        private void EnsureSocket()
        {
            if (socket is null)
            {
                socket = Util.CreateUnixDomainSocket();
                socket.Blocking = false;
                socket.Bind(new UnixDomainSocketEndPoint(file));
                socket.Listen(connectionQueueSize);
            }
        }

        private void Dispose(bool disposing)
        {
            Console.WriteLine("Disposing a domain socket server");

            if (disposing)
            {
                cancellationSource.Cancel();
                socket.SafeDispose();
                socket = null;
            }

            if(!Util.TryDeleteFile(file))
                Console.WriteLine("Failed to delete a socket's backing file");
        }
    }
}
