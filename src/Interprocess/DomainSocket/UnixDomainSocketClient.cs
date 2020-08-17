using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static Cloudtoid.Interprocess.DomainSocket.UnixDomainSocketUtil;

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
            logger.LogDebug("Creating a domain socket client - {0}", file);
            endpoint = CreateUnixDomainSocketEndPoint(file);
            socket = CreateUnixDomainSocket(blocking: false);
        }

        public void Dispose()
        {
            logger.LogDebug("Disposing a domain socket client - {0}", file);
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
                    logger.LogInformation(
                        "Disposing a Unix Domain Socket as it is no longer connected. Endpoint = {0}. IsCancelled = {1}",
                        file,
                        source.Token.IsCancellationRequested);

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
            catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset)
            {
                logger.LogWarning("Reading from a Unix Domain Socket failed but the client can re-establish the connection.");
                throw;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.OperationAborted)
            {
                logger.LogInformation("Reading from a Unix Domain Socket was cancelled.");
                throw new OperationCanceledException();
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
#pragma warning disable VSTHRD103
                    socket.Connect(endpoint);
#pragma warning restore VSTHRD103
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
                    if (!File.Exists(file))
                        throw;

                    var duration = (DateTime.Now - startTime).Milliseconds;
                    if (duration > ConnectMillisecondTimeout)
                    {
                        logger.LogError(
                            $"Found an orphaned Unix Domain Socket backing file. '{file}'" +
                            "This can only happen if an earlier process terminated without deleting the file. " +
                            "This should be treated as a bug.");

                        PathUtil.TryDeleteFile(file);
                        throw;
                    }

                    await Task.Delay(5, cancellation).ConfigureAwait(false);
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

                var newSocket = CreateUnixDomainSocket(blocking: false);
                if (Interlocked.CompareExchange(ref socket, newSocket, null) == null)
                    return newSocket;
            }
        }
    }
}
