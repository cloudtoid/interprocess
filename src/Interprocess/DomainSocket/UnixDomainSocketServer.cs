using System;
using System.Net.Sockets;
using System.Threading;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal sealed class UnixDomainSocketServer : IDisposable
    {
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly string file;
        private readonly Socket socket;

        internal UnixDomainSocketServer(string file, int connectionQueueSize = 100)
        {
            this.file = file;
            socket = Util.CreateUnixDomainSocket(blocking: false);
            socket.Bind(Util.CreateUnixDomainSocketEndPoint(file));
            socket.Listen(connectionQueueSize);
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
                catch (SocketException se) when (se.SocketErrorCode == SocketError.OperationAborted)
                {
                    Console.WriteLine("Socket accept operation cancelled.");
                    throw new OperationCanceledException();
                }
            }
        }

        private void Dispose(bool disposing)
        {
            Console.WriteLine("Disposing a domain socket server");

            if (disposing)
            {
                cancellationSource.Cancel();
                socket.SafeDispose();
            }

            if(!Util.TryDeleteFile(file))
                Console.WriteLine("Failed to delete a socket's backing file");
        }
    }
}
