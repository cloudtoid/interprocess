using Cloudtoid.Interprocess.Semaphore.Unix;
using FluentAssertions;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class SemaphoreTests
    {
        private const string DefaultQueueName = "qn";
        private static readonly string path = Path.GetTempPath();
        private static readonly SharedAssetsIdentifier defaultIdentifier = new SharedAssetsIdentifier(DefaultQueueName, path);

        [Fact]
        public async Task CanDisposeUnixServer()
        {
            // simple create and dispose
            using (new UnixSemaphore.Server(defaultIdentifier))
            {
            }

            using (var server = new UnixSemaphore.Server(defaultIdentifier))
            {
                await server.SignalAsync(default);
            }

            using (var server = new UnixSemaphore.Server(defaultIdentifier))
            {
                await server.SignalAsync(default);
                await Task.Delay(500);
            }
        }

        [Fact]
        public void CanDisposeUnixClient()
        {
            // simple create and dispose
            using (new UnixSemaphore.Client(defaultIdentifier))
            {
            }

            using (var client = new UnixSemaphore.Client(defaultIdentifier))
            {
                client.Wait(50).Should().BeFalse();
            }

            using (var client = new UnixSemaphore.Client(defaultIdentifier))
            {
                client.Wait(500).Should().BeFalse();
            }
        }

        [Fact]
        public async Task CanSignalMultipleClients()
        {
            using var server = new UnixSemaphore.Server(defaultIdentifier);
            using var client1 = new UnixSemaphore.Client(defaultIdentifier);
            using var client2 = new UnixSemaphore.Client(defaultIdentifier);

            await WaitForClientCount(server, 2);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            for (int i = 0; i < 100; i++)
            {
                await server.SignalAsync(default);

                client1.Wait(1000).Should().BeTrue();
                client2.Wait(1000).Should().BeTrue();
            }

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
        }

        [Fact]
        public async Task CanAddClientLater()
        {
            using var server = new UnixSemaphore.Server(defaultIdentifier);
            using var client1 = new UnixSemaphore.Client(defaultIdentifier);
            using var client2 = new UnixSemaphore.Client(defaultIdentifier);

            await WaitForClientCount(server, 2);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            await server.SignalAsync(default);

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();

            using var client3 = new UnixSemaphore.Client(defaultIdentifier);
            await WaitForClientCount(server, 3);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            await server.SignalAsync(default);

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();
        }

        [Fact]
        public async Task CanSupportManyClients()
        {
            const int Count = 10;

            using var server = new UnixSemaphore.Server(defaultIdentifier);
            var clients = new UnixSemaphore.Client[Count];

            for (int i = 0; i < Count; i++)
                clients[i] = new UnixSemaphore.Client(defaultIdentifier);

            await WaitForClientCount(server, Count);
            await server.SignalAsync(default);

            for (int i = 0; i < Count; i++)
                clients[i].Dispose();

            await server.SignalAsync(default);
            await WaitForClientCount(server, 0);
        }

        [Fact]
        public async Task CanConnectMultipleClientsToMultipleServer()
        {
            using var server1 = new UnixSemaphore.Server(defaultIdentifier);
            using var client1 = new UnixSemaphore.Client(defaultIdentifier);
            using var client2 = new UnixSemaphore.Client(defaultIdentifier);

            await WaitForClientCount(server1, 2);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            var start = DateTime.Now;
            await server1.SignalAsync(default);

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            Console.WriteLine("Signal 1 - " + (DateTime.Now - start).TotalMilliseconds);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();

            start = DateTime.Now;
            await server1.SignalAsync(default);

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            Console.WriteLine("Signal 2 - " + (DateTime.Now - start).TotalMilliseconds);

            using var client3 = new UnixSemaphore.Client(defaultIdentifier);
            await WaitForClientCount(server1, 3);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            start = DateTime.Now;
            await server1.SignalAsync(default);

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();
            Console.WriteLine("Signal 3 - " + (DateTime.Now - start).TotalMilliseconds);

            using var server2 = new UnixSemaphore.Server(defaultIdentifier);
            await WaitForClientCount(server2, 3);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            start = DateTime.Now;
            await server2.SignalAsync(default);

            client1.Wait(1000).Should().BeTrue();
            client2.Wait(1000).Should().BeTrue();
            client3.Wait(1000).Should().BeTrue();
            Console.WriteLine("Signal 4 - " + (DateTime.Now - start).TotalMilliseconds);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            start = DateTime.Now;
            for (int i = 0; i < 10000; i++)
            {
                await server1.SignalAsync(default);

                client1.Wait(1000).Should().BeTrue();
                client2.Wait(1000).Should().BeTrue();
                client3.Wait(1000).Should().BeTrue();
            }
            Console.WriteLine("Signal 5 (Average) - " + (DateTime.Now - start).TotalMilliseconds / 10000);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            start = DateTime.Now;
            await server1.SignalAsync(default);
            await server1.SignalAsync(default);

            client1.Wait(1000).Should().BeTrue("1");
            client2.Wait(1000).Should().BeTrue("2");
            client3.Wait(1000).Should().BeTrue("3");

            client1.Wait(1000).Should().BeTrue("4");
            client2.Wait(1000).Should().BeTrue("5");
            client3.Wait(1000).Should().BeTrue("6");
            Console.WriteLine("Signal 6 - " + (DateTime.Now - start).TotalMilliseconds);

            start = DateTime.Now;
            await server1.SignalAsync(default);
            await server2.SignalAsync(default);

            client1.Wait(1000).Should().BeTrue("1");
            client2.Wait(1000).Should().BeTrue("2");
            client3.Wait(1000).Should().BeTrue("3");

            client1.Wait(1000).Should().BeTrue("4");
            client2.Wait(1000).Should().BeTrue("5");
            client3.Wait(1000).Should().BeTrue("6");
            Console.WriteLine("Signal 7 - " + (DateTime.Now - start).TotalMilliseconds);

            client1.Wait(50).Should().BeFalse();
            client2.Wait(50).Should().BeFalse();
            client3.Wait(50).Should().BeFalse();

            Console.WriteLine("Disposing all");
        }

        [Fact]
        public async Task UnixSignalTests()
        {
            using (var semaphore = new UnixSemaphore(defaultIdentifier))
            {
                semaphore.WaitOne(1).Should().BeTrue();
                semaphore.WaitOne(1).Should().BeFalse();

                await semaphore.ReleaseAsync(default);

                semaphore.WaitOne(100).Should().BeTrue();
                semaphore.WaitOne(1).Should().BeFalse();
            }
        }

        private static async Task WaitForClientCount(
            UnixSemaphore.Server server,
            int count)
        {
            while (server.ClientCount != count)
                await Task.Delay(5);
        }
    }
}
