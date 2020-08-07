using Cloudtoid.Interprocess.DomainSocket;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class DomainSocketTests
    {
        private static readonly ReadOnlyMemory<byte> message = new byte[] { 1 };

        [Fact]
        public void CanCreateUnixDomainSocket()
        {
            using var socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
            socket.AddressFamily.Should().Be(AddressFamily.Unix);
            socket.SocketType.Should().Be(SocketType.Stream);
            socket.ProtocolType.Should().Be(ProtocolType.Unspecified);
        }

        [Fact]
        public void CanSafeDispose()
        {
            var socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
            socket.SafeDispose();
            socket = null;
            socket.SafeDispose();
        }

        [Fact]
        public async Task CanAcceptConnections()
        {
            using var source = new CancellationTokenSource();

            var file = GetRandomNonExistingFilePath();
            var endpoint = new UnixDomainSocketEndPoint(file);

            var connections = new List<Socket>();
            using var cancelled = new ManualResetEventSlim();

            Task task;
            using (var server = new UnixDomainSocketServer(file))
            {
                task = AcceptLoopAsync(server, s => connections.Add(s), () => cancelled.Set(), source.Token);

                using (var client = UnixDomainSocketUtil.CreateUnixDomainSocket())
                {
                    client.Connect(endpoint);
                    client.Connected.Should().BeTrue();
                }

                using (var client = UnixDomainSocketUtil.CreateUnixDomainSocket())
                {
                    client.Connect(endpoint);
                    client.Connected.Should().BeTrue();
                }

                using (var client1 = UnixDomainSocketUtil.CreateUnixDomainSocket())
                using (var client2 = UnixDomainSocketUtil.CreateUnixDomainSocket())
                {
                    client1.Connect(endpoint);
                    client1.Connected.Should().BeTrue();

                    client2.Connect(endpoint);
                    client2.Connected.Should().BeTrue();
                }

                while (connections.Count < 4)
                    await Task.Delay(10);
            }

            cancelled.Wait(500).Should().BeTrue();
            await task;
            connections.Should().HaveCount(4);
        }

        [Fact]
        public async Task CanAcceptConnectionsTimeout()
        {
            var file = GetRandomNonExistingFilePath();
            var endpoint = new UnixDomainSocketEndPoint(file);

            using (var source = new CancellationTokenSource())
            using (var cancelled = new ManualResetEventSlim())
            using (var server = new UnixDomainSocketServer(file))
            {
                source.Cancel();
                await AcceptLoopAsync(server, s => { }, () => cancelled.Set(), source.Token);
                cancelled.Wait(1).Should().BeTrue();
            }

            using (var source = new CancellationTokenSource())
            using (var cancelled = new ManualResetEventSlim())
            using (var server = new UnixDomainSocketServer(file))
            {
                source.CancelAfter(200);
                await AcceptLoopAsync(server, s => { }, () => cancelled.Set(), source.Token);
                cancelled.Wait(1).Should().BeTrue();
            }

            using (var source = new CancellationTokenSource())
            using (var cancelled = new ManualResetEventSlim())
            using (var server = new UnixDomainSocketServer(file))
            {
                source.CancelAfter(1000);
                await AcceptLoopAsync(server, s => { }, () => cancelled.Set(), source.Token);
                cancelled.Wait(1).Should().BeTrue();
            }
        }

        [Fact]
        public async Task CanAcceptConnectionsRecoverFromTimeout()
        {
            var file = GetRandomNonExistingFilePath();
            var endpoint = new UnixDomainSocketEndPoint(file);
            Task task;

            using (var source = new CancellationTokenSource())
            using (var source2 = new CancellationTokenSource())
            using (var cancelled = new ManualResetEventSlim())
            using (var server = new UnixDomainSocketServer(file))
            {
                source.Cancel();
                await AcceptLoopAsync(server, s => { }, () => cancelled.Set(), source.Token);
                cancelled.Wait(1).Should().BeTrue();

                task = AcceptLoopAsync(server, s => { }, () => cancelled.Set(), source2.Token);
                using (var client = UnixDomainSocketUtil.CreateUnixDomainSocket())
                {
                    client.Connect(endpoint);
                    client.Connected.Should().BeTrue();
                }

                File.Exists(file).Should().BeTrue();
            }
            File.Exists(file).Should().BeFalse();
            await task;
        }

        [Fact]
        public void ServerDoesNotCreateFile()
        {
            var file = GetRandomNonExistingFilePath();
            using (new UnixDomainSocketServer(file))
            {
                File.Exists(file).Should().BeFalse();
            }
        }

        [Fact]
        public void ClientDoesNotCreateFile()
        {
            var file = GetRandomNonExistingFilePath();
            using (new UnixDomainSocketClient(file))
            {
                File.Exists(file).Should().BeFalse();
            }
        }

        [Fact]
        public async Task ClientUnableToConnectWithoutServer()
        {
            var file = GetRandomNonExistingFilePath();
            using (var client = new UnixDomainSocketClient(file))
            {
                Func<Task<int>> action = async () => await client.ReceiveAsync(new byte[1], default);
                var ex = await action.Should().ThrowAsync<SocketException>();
                ex.Where(se => se.SocketErrorCode == SocketError.AddressNotAvailable);
                File.Exists(file).Should().BeFalse();
            }
        }

        [Fact]
        public async Task CanReceiveAsync()
        {
            var file = GetRandomNonExistingFilePath();
            var endpoint = new UnixDomainSocketEndPoint(file);

            using (var server = new UnixDomainSocketServer(file))
            using (var client = new UnixDomainSocketClient(file))
            {
                Socket socket;
                var task = Task.Run(async () =>
                {
                    socket = await server.AcceptAsync(default);
                    var c = await socket.SendAsync(message, default);
                    c.Should().Be(1);
                });

                await Task.Delay(200);
                var buffer = new byte[1];
                var count = await client.ReceiveAsync(buffer, default);
                count.Should().Be(1);
                buffer[0].Should().Be(1);
            }
        }

        private async Task AcceptLoopAsync(
            UnixDomainSocketServer server,
            Action<Socket> onNewConnection,
            Action onCancelled,
            CancellationToken cancellation)
        {
            while (true)
            {
                Socket socket;
                try
                {
                    socket = await server.AcceptAsync(cancellation);
                }
                catch (OperationCanceledException)
                {
                    onCancelled();
                    return;
                }
                onNewConnection(socket);
            }
        }

        private static string GetRandomNonExistingFilePath()
        {
            string result;
            do
            {
                result = Path.GetRandomFileName();
            }
            while (File.Exists(result));

            return result;
        }

        private sealed class AsyncResult : IAsyncResult
        {
            private readonly ManualResetEvent waitHandle;

            public AsyncResult(bool completed)
            {
                IsCompleted = completed;
                waitHandle = new ManualResetEvent(completed);
            }

            public object? AsyncState => null;

            public WaitHandle AsyncWaitHandle => waitHandle;

            public bool CompletedSynchronously => IsCompleted;

            public bool IsCompleted { get; }
        }
    }
}
