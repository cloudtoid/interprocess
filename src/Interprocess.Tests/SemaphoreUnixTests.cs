using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.Semaphore.Unix;
using FluentAssertions;
using Microsoft.Extensions.Logging;
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
        public async Task CanDisposeUnixServerAsync()
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
                client.Wait(50).Should().BeFalse();
            }

            using (var client = new SemaphoreWaiter(fixture.Identifier, loggerFactory))
            {
                client.Wait(500).Should().BeFalse();
            }
        }

        [Fact]
        public async Task CanSignalMultipleClientsAsync()
        {
            using var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCountAsync(server, 2);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            const int Count = 10000;
            for (var i = 0; i < Count; i++)
            {
                server.Release();

                client1.Wait(1000).Should().BeTrue();
                client2.Wait(1000).Should().BeTrue();
            }

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
        }

        [Fact]
        public async Task CanReceiveSignalsAtDifferentPacesAsync()
        {
            using var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCountAsync(server, 2);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            const int Count = 10000;
            for (var i = 0; i < Count; i++)
            {
                server.Release();
                client1.Wait(1000).Should().BeTrue();
            }

            client1.Wait(50).Should().BeFalse();

            for (var i = 0; i < Count; i++)
                client2.Wait(1000).Should().BeTrue();

            client2.Wait(50).Should().BeFalse();
        }

        [Fact]
        public async Task CanAddClientLaterAsync()
        {
            using var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCountAsync(server, 2);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            server.Release();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();

            using var client3 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            await WaitForClientCountAsync(server, 3);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            server.Release();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();
        }

        [Fact]
        public async Task CanSupportManyClientsAsync()
        {
            const int Count = 20;

            using var server = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            var clients = new SemaphoreWaiter[Count];

            for (var i = 0; i < Count; i++)
                clients[i] = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCountAsync(server, Count);
            server.Release();

            for (var i = 0; i < Count; i++)
                clients[i].Dispose();

            while (server.ClientCount > 0)
                server.Release();
        }

        [Theory]
        [Repeat(2)]
        [SuppressMessage("Style", "IDE0060", Justification = "Parameter is needed for xUnit's repeated test to work.")]
        [SuppressMessage("Usage", "xUnit1026", Justification = "Parameter is needed for xUnit's repeated test to work.")]
        public async Task CanSupporrtMultipleServersAndClientsAsync(int i)
        {
            using var server1 = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCountAsync(server1, 2);

            server1.Release();
            client1.Wait(2000).Should().BeTrue();
            client2.Wait(2000).Should().BeTrue();

            using var server2 = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            await WaitForClientCountAsync(server2, 2);

            server1.Release();
            client1.Wait(2000).Should().BeTrue();
            client2.Wait(2000).Should().BeTrue();

            server2.Release();
            client1.Wait(2000).Should().BeTrue();
            client2.Wait(2000).Should().BeTrue();

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            server1.Release();
            server2.Release();
            client1.Wait(2000).Should().BeTrue();
            client1.Wait(2000).Should().BeTrue();
            client2.Wait(2000).Should().BeTrue();
            client2.Wait(2000).Should().BeTrue();

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
        }

        // this is complex test that sends and receives many times in a variety
        // of manners. every single scenario has a separate unit test in this
        // file but here we combine many of them into a single test
        [Fact]
        public async Task CanPerformManyActionsAsync()
        {
            using var server1 = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            using var client1 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            using var client2 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);

            await WaitForClientCountAsync(server1, 2);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            server1.Release();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            server1.Release();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();

            using var client3 = new SemaphoreWaiter(fixture.Identifier, loggerFactory);
            await WaitForClientCountAsync(server1, 3);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            server1.Release();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();

            using var server2 = new SemaphoreReleaser(fixture.Identifier, loggerFactory);
            await WaitForClientCountAsync(server2, 3);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            server2.Release();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            for (var i = 0; i < 10000; i++)
            {
                server1.Release();

                client1.Wait(1000).Should().BeTrue();
                client2.Wait(1000).Should().BeTrue();
                client3.Wait(1000).Should().BeTrue();
            }

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            server1.Release();
            server1.Release();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();

            server1.Release();
            server2.Release();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();
        }

        private static async Task WaitForClientCountAsync(SemaphoreReleaser server, int count)
        {
            while (server.ClientCount != count)
                await Task.Delay(5);
        }
    }
}
