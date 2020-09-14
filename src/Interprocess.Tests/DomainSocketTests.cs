﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.DomainSocket;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using static Cloudtoid.Interprocess.DomainSocket.UnixDomainSocketUtil;

namespace Cloudtoid.Interprocess.Tests
{
    public class DomainSocketTests
    {
        private static readonly ReadOnlyMemory<byte> Message = new byte[] { 1 };
        private readonly ILoggerFactory loggerFactory;

        public DomainSocketTests(ITestOutputHelper testOutputHelper)
        {
            loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new XunitLoggerProvider(testOutputHelper));
        }

        [Fact]
        public void CanCreateUnixDomainSocket()
        {
            using var socket = CreateUnixDomainSocket();
            socket.AddressFamily.Should().Be(AddressFamily.Unix);
            socket.SocketType.Should().Be(SocketType.Stream);
            socket.ProtocolType.Should().Be(ProtocolType.Unspecified);
        }

        [Fact]
        public void CanSafeDispose()
        {
            var socket = CreateUnixDomainSocket();
            socket.SafeDispose();
            socket = null;
            socket.SafeDispose();
        }

        [Fact]
        public async Task CanAcceptConnectionsAsync()
        {
            using var source = new CancellationTokenSource();

            var file = GetRandomNonExistingFilePath();
            var endpoint = new UnixDomainSocketEndPoint(file);

            var connections = new List<Socket>();
            using var cancelled = new ManualResetEventSlim();

            Task task;
            using (var server = new UnixDomainSocketServer(file, loggerFactory))
            {
                task = Task.Run(() => AcceptLoop(server, s => connections.Add(s), () => cancelled.Set(), source.Token));

                using (var client = CreateUnixDomainSocket())
                {
                    client.Connect(endpoint);
                    client.Connected.Should().BeTrue();
                }

                using (var client = CreateUnixDomainSocket())
                {
                    client.Connect(endpoint);
                    client.Connected.Should().BeTrue();
                }

                using (var client1 = CreateUnixDomainSocket())
                using (var client2 = CreateUnixDomainSocket())
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
        public void CanAcceptConnectionsTimeout()
        {
            var file = GetRandomNonExistingFilePath();
            var endpoint = new UnixDomainSocketEndPoint(file);

            using (var source = new CancellationTokenSource())
            using (var cancelled = new ManualResetEventSlim())
            using (var server = new UnixDomainSocketServer(file, loggerFactory))
            {
                source.Cancel();
                AcceptLoop(server, s => { }, () => cancelled.Set(), source.Token);
                cancelled.Wait(1).Should().BeTrue();
            }

            using (var source = new CancellationTokenSource())
            using (var cancelled = new ManualResetEventSlim())
            using (var server = new UnixDomainSocketServer(file, loggerFactory))
            {
                source.CancelAfter(200);
                AcceptLoop(server, s => { }, () => cancelled.Set(), source.Token);
                cancelled.Wait(1).Should().BeTrue();
            }

            using (var source = new CancellationTokenSource())
            using (var cancelled = new ManualResetEventSlim())
            using (var server = new UnixDomainSocketServer(file, loggerFactory))
            {
                source.CancelAfter(1000);
                AcceptLoop(server, s => { }, () => cancelled.Set(), source.Token);
                cancelled.Wait(1).Should().BeTrue();
            }
        }

        [Fact]
        public async Task CanAcceptConnectionsRecoverFromTimeoutAsync()
        {
            var file = GetRandomNonExistingFilePath();
            var endpoint = new UnixDomainSocketEndPoint(file);
            Task task;

            using (var source = new CancellationTokenSource())
            using (var source2 = new CancellationTokenSource())
            using (var cancelled = new ManualResetEventSlim())
            using (var server = new UnixDomainSocketServer(file, loggerFactory))
            {
                source.Cancel();
                AcceptLoop(server, s => { }, () => cancelled.Set(), source.Token);
                cancelled.Wait(1).Should().BeTrue();

                task = Task.Run(() => AcceptLoop(server, s => { }, () => { }, source2.Token));
                await Task.Delay(100);
                using (var client = CreateUnixDomainSocket())
                {
                    client.Connect(endpoint);
                    client.Connected.Should().BeTrue();
                }

                File.Exists(file).Should().BeTrue();
            }

            await task;
            File.Exists(file).Should().BeFalse();
        }

        [Fact]
        public void ServerCreatesFile()
        {
            var file = GetRandomNonExistingFilePath();
            using (new UnixDomainSocketServer(file, loggerFactory))
            {
                File.Exists(file).Should().BeTrue();
            }
        }

        [Fact]
        public void ClientDoesNotCreateFile()
        {
            var file = GetRandomNonExistingFilePath();
            using (new UnixDomainSocketClient(file, loggerFactory))
            {
                File.Exists(file).Should().BeFalse();
            }
        }

        [Fact]
        public async Task ClientUnableToConnectWithoutServerAsync()
        {
            var file = GetRandomNonExistingFilePath();
            using (var client = new UnixDomainSocketClient(file, loggerFactory))
            {
                Func<Task<int>> action = async () => await client.ReceiveAsync(new byte[1], default);
                var ex = await action.Should().ThrowAsync<SocketException>();
                ex.Where(se => se.SocketErrorCode == SocketError.AddressNotAvailable || se.SocketErrorCode == SocketError.ConnectionRefused);
                File.Exists(file).Should().BeFalse();
            }
        }

        [Fact]
        public async Task CanReceiveAsync()
        {
            var file = GetRandomNonExistingFilePath();
            var endpoint = new UnixDomainSocketEndPoint(file);

            using (var server = new UnixDomainSocketServer(file, loggerFactory))
            using (var client = new UnixDomainSocketClient(file, loggerFactory))
            {
                Socket socket;
                var task = Task.Run(async () =>
                {
                    socket = server.Accept(default);
                    var c = await socket.SendAsync(Message, default);
                    c.Should().Be(1);
                });

                await Task.Delay(200);
                var buffer = new byte[1];
                var count = await client.ReceiveAsync(buffer, default);
                count.Should().Be(1);
                buffer[0].Should().Be(1);
            }
        }

        private void AcceptLoop(
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
                    socket = server.Accept(cancellation);
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
                result = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
