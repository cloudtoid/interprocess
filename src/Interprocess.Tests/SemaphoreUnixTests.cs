using Cloudtoid.Interprocess.Semaphore.Unix;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Cloudtoid.Interprocess.Tests
{
    public class SemaphoreUnixTests : IClassFixture<UniqueIdentifierFixture>
    {
        private readonly UniqueIdentifierFixture fixture;
        private readonly ILoggerFactory loggerFactory;

        public SemaphoreUnixTests(
            UniqueIdentifierFixture fixture,
            ITestOutputHelper testOutputHelper)
        {
            this.fixture = fixture;
            loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new XunitLoggerProvider(testOutputHelper));
        }

        [Fact]
        public async Task CanDisposeUnixServer()
        {
            // simple create and dispose
            using (new SemaphoreReleaser(fixture.Identifier, loggerFactory))
            {
            }

            using (var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory))
            {
                server.Release();
            }

            using (var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory))
            {
                server.Release();
                await Task.Delay(500);
            }

            using (var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory))
            {
                await Task.Delay(500);
                server.Release();
                await Task.Delay(500);
            }
        }

        [Fact]
        public void CanDisposeUnixClient()
        {
            // simple create and dispose
            using (new SemaphoreWaiter(fixture.Identifier, loggerFactory))
            {
            }

            using (var client = new SemaphoreWaiter(fixture.Identifier, loggerFactory))
            {
                client.WaitOne(50).Should().BeFalse();
            }

            using (var client = new SemaphoreWaiter(fixture.Identifier, loggerFactory))
            {
                client.WaitOne(500).Should().BeFalse();
            }
        }

        [Fact]
        public async Task CanSignalMultipleClients()
        {
            using var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCount(server, 2);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            const int Count = 10000;
            for (int i = 0; i < Count; i++)
            {
                server.Release();

                client1.WaitOne(1000).Should().BeTrue();
                client2.WaitOne(1000).Should().BeTrue();
            }

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
        }

        [Fact]
        public async Task CanReceiveSignalsAtDifferentPaces()
        {
            using var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCount(server, 2);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            const int Count = 10000;
            for (int i = 0; i < Count; i++)
            {
                server.Release();
                client1.WaitOne(1000).Should().BeTrue();
            }

            client1.WaitOne(50).Should().BeFalse();

            for (int i = 0; i < Count; i++)
                client2.WaitOne(1000).Should().BeTrue();

            client2.WaitOne(50).Should().BeFalse();
        }

        [Fact]
        public async Task CanAddClientLater()
        {
            using var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCount(server, 2);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            server.Release();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            using var client3 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            await WaitForClientCount(server, 3);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            server.Release();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();
        }

        [Fact]
        public async Task CanSupportManyClients()
        {
            const int Count = 20;

            using var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            var clients = new SemaphoreWaiter[Count];

            for (int i = 0; i < Count; i++)
                clients[i] = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCount(server, Count);
            server.Release();

            for (int i = 0; i < Count; i++)
                clients[i].Dispose();

            while (server.ClientCount > 0)
                server.Release();
        }

        [Theory]
        [Repeat(100)]
        public async Task CanSupporrtMultipleServersAndClients(int i)
        {
            Console.WriteLine(i);
            using var server1 = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCount(server1, 2);

            server1.Release();
            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            using var server2 = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            await WaitForClientCount(server2, 2);

            server1.Release();
            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            server2.Release();
            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            server1.Release();
            server2.Release();
            client1.WaitOne(1000).Should().BeTrue();
            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
        }

        // this is complex test that sends and receives many times in a variety
        // of manners. every single scenario has a separate unit test in this
        // file but here we combine many of them into a single test
        [Fact]
        public async Task CanPerformManyActions()
        {
            using var server1 = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCount(server1, 2);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            server1.Release();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            server1.Release();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            using var client3 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            await WaitForClientCount(server1, 3);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            server1.Release();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();

            using var server2 = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            await WaitForClientCount(server2, 3);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            server2.Release();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            for (int i = 0; i < 10000; i++)
            {
                server1.Release();

                client1.WaitOne(1000).Should().BeTrue();
                client2.WaitOne(1000).Should().BeTrue();
                client3.WaitOne(1000).Should().BeTrue();
            }

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            server1.Release();
            server1.Release();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();

            server1.Release();
            server2.Release();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();
        }

        private static async Task WaitForClientCount(SemaphoreReleaser server, int count)
        {
            while (server.ClientCount != count)
                await Task.Delay(5);
        }
    }
}
