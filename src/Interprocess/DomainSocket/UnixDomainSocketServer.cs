using Microsoft.Extensions.Logging;
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
        private readonly ILogger logger;

        internal UnixDomainSocketServer(
            string file,
            ILogger logger,
            int connectionQueueSize = 100)
        {
            this.file = file;
            this.logger = logger;
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
                    logger.LogInformation(se, "Accepting a Unix Domain Socket connection was cancelled.");
                    throw new OperationCanceledException();
                }
                catch (OperationCanceledException oce)
                {
                    logger.LogInformation(oce, "Accepting a Unix Domain Socket connection was cancelled.");
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Accepting a Unix Domain Socket connection failed unexpectedly.");
                    throw;
                }
            }
        }

        private void Dispose(bool disposing)
        {
            logger.LogDebug("Disposing a domain socket server");

            if (disposing)
            {
                cancellationSource.Cancel();
                socket.SafeDispose();
            }

            if (!Util.TryDeleteFile(file))
                logger.LogError("Failed to delete a Unix Domain Socket's backing file.");
        }
    }
}
