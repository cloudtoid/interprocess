using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal sealed class UnixDomainSocketClient : IDisposable
    {
        private const int ConnectMillisecondTimeout = 500;
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly UnixDomainSocketEndPoint endpoint;
        private readonly string file;
        private readonly ILogger<UnixDomainSocketClient> logger;
        private Socket? socket;

        internal UnixDomainSocketClient(string file, ILoggerFactory loggerFactory)
        {
            this.file = file;
            logger = loggerFactory.CreateLogger<UnixDomainSocketClient>();
            endpoint = Util.CreateUnixDomainSocketEndPoint(file);
            socket = Util.CreateUnixDomainSocket(blocking: false);
        }

        public void Dispose()
        {
            logger.LogInformation($"Disposing a domain socket client - {file}");
            cancellationSource.Cancel();
            Interlocked.Exchange(ref socket, null).SafeDispose();
        }

        internal async ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();

            using var source = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationSource.Token,
                cancellation);

            var socket = GetSocket();

            try
            {
                await EnsureConnectedAsync(socket, source.Token).ConfigureAwait(false);
                return await ReceiveAsync(socket, buffer, source.Token).ConfigureAwait(false);
            }
            finally
            {
                if (!socket.Connected)
                {
                    logger.LogInformation($"Disposing a Unix Domain Socket because it is no longer connected. Endpoint = {file}. IsCancelled = {source.Token.IsCancellationRequested}");
                    Interlocked.CompareExchange(ref this.socket, null, socket).SafeDispose(logger);
                }
            }
        }

        private async ValueTask<int> ReceiveAsync(
            Socket socket,
            Memory<byte> buffer,
            CancellationToken cancellation)
        {
            try
            {
                return await socket.ReceiveAsync(buffer, SocketFlags.None, cancellation).ConfigureAwait(false);
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.OperationAborted)
            {
                logger.LogInformation("Reading from a Unix Domain Socket was cancelled.");
                throw new OperationCanceledException();
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset)
            {
                logger.LogWarning("Reading from a Unix Domain Socket failed but the client can re-establish the connection.");
                throw;
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Reading from a Unix Domain Socket was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reading from a Unix Domain Socket failed unexpectedly.");
                throw;
            }
        }

        private async Task EnsureConnectedAsync(
            Socket socket,
            CancellationToken cancellation)
        {
            var startTime = DateTime.Now;
            while (!socket.Connected)
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
                    {
                        logger.LogError("A Unix Domain Socket client failed to connect to the server because the timeout expired.");
                        throw new TimeoutException("A Unix Domain Socket client failed to connect to the server because the timeout expired.");
                    }

                    await Task.Delay(5, cancellation).ConfigureAwait(false);
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.AddressNotAvailable || se.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    if (File.Exists(file))
                    {
                        logger.LogError(
                            "Found an orphaned Unix Domain Socket backing file lock file. " +
                            "This can only happen if an earlier process terminated without deleting the file. " +
                            "This should be treated as a bug.");

                        Util.TryDeleteFile(file);
                    }

                    throw;
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.OperationAborted)
                {
                    logger.LogInformation("Connecting to a Unix Domain Socket was cancelled.");
                    throw new OperationCanceledException();
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Connecting to a Unix Domain Socket was cancelled.");
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Connecting to a Unix Domain Socket failed unexpectedly.");
                    throw;
                }
            }
        }

        private Socket GetSocket()
        {
            while (true)
            {
                var snapshot = socket;
                if (snapshot != null)
                    return snapshot;

                var newSocket = Util.CreateUnixDomainSocket(blocking: false);
                if (Interlocked.CompareExchange(ref socket, newSocket, null) == null)
                    return newSocket;
            }
        }
    }
}
