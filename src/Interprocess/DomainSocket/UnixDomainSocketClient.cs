using System;
using System.Diagnostics.CodeAnalysis;
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

        [SuppressMessage("CodeQuality", "IDE0069:Disposable fields should be disposed", Justification = "This is an incorrect warning as we call into SafeDispose()")]
        private Socket? socket;

        internal UnixDomainSocketClient(string file, ILoggerFactory loggerFactory)
        {
            this.file = file;
            logger = loggerFactory.CreateLogger<UnixDomainSocketClient>();
            logger.LogDebug("Creating a domain socket client - {0}", file);
            endpoint = CreateUnixDomainSocketEndPoint(file);
#if NET5_0
            socket = CreateUnixDomainSocket(blocking: true);
#else
            socket = CreateUnixDomainSocket(blocking: false);
#endif
        }

        public void Dispose()
        {
            logger.LogDebug("Disposing a domain socket client - {0}", file);
            cancellationSource.Cancel();
            Interlocked.Exchange(ref socket, null).SafeDispose();
            cancellationSource.Dispose();
        }

        internal async ValueTask<int> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellation)
        {
            var socket = GetSocket();
            using var source = new LinkedCancellationToken(cancellationSource.Token, cancellation);
            cancellation = source.Token;

            try
            {
                await EnsureConnectedAsync(socket, cancellation).ConfigureAwait(false);
                return await ReceiveAsync(socket, buffer, cancellation).ConfigureAwait(false);
            }
            finally
            {
                if (!socket.Connected)
                {
                    logger.LogDebug(
                        "Disposing a Unix Domain Socket as it is no longer connected. Endpoint = {0}. IsCancelled = {1}",
                        file,
                        cancellation.IsCancellationRequested);

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
                logger.LogDebug("Reading from a Unix Domain Socket was cancelled.");
                throw new OperationCanceledException();
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Reading from a Unix Domain Socket was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reading from a Unix Domain Socket failed unexpectedly.");
                throw;
            }
        }

        private async ValueTask EnsureConnectedAsync(
            Socket socket,
            CancellationToken cancellation)
        {
            var startTime = DateTime.Now;
            while (!socket.Connected)
            {
                cancellation.ThrowIfCancellationRequested();

#if NET5_0
                try
                {
                    await socket.ConnectAsync(endpoint, cancellation).ConfigureAwait(false);
                }
#else
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
#endif
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
                    logger.LogDebug("Connecting to a Unix Domain Socket was cancelled.");
                    throw new OperationCanceledException();
                }
                catch (OperationCanceledException)
                {
                    logger.LogDebug("Connecting to a Unix Domain Socket was cancelled.");
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
