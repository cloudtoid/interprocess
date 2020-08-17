using System;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using static Cloudtoid.Interprocess.DomainSocket.UnixDomainSocketUtil;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal sealed class UnixDomainSocketServer : IDisposable
    {
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly string file;
        private readonly Socket socket;
        private readonly ILogger<UnixDomainSocketServer> logger;

        internal UnixDomainSocketServer(
            string file,
            ILoggerFactory loggerFactory,
            int connectionQueueSize = 100)
        {
            this.file = file;
            logger = loggerFactory.CreateLogger<UnixDomainSocketServer>();
            logger.LogDebug("Creating a domain socket server - {0}", file);
            socket = CreateUnixDomainSocket(blocking: false);

            try
            {
                socket.Bind(CreateUnixDomainSocketEndPoint(file));
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.OperationNotSupported)
            {
                logger.LogError(se, $"Failed to bind to a Unix Domain Socket at '{file}'. " +
                    "This typically happens if the path is not supported by this OS for Domain Sockets. " +
                    "Consider changing the path that is passed to the queue.");

                throw;
            }

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
                    logger.LogInformation("Accepting a Unix Domain Socket connection was cancelled.");
                    throw new OperationCanceledException();
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Accepting a Unix Domain Socket connection was cancelled.");
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
            logger.LogDebug("Disposing a domain socket server - {0}", file);

            if (disposing)
            {
                cancellationSource.Cancel();
                socket.SafeDispose(logger);
            }

            if (!PathUtil.TryDeleteFile(file))
                logger.LogError("Failed to delete a Unix Domain Socket's backing file.");
        }
    }
}
