using Cloudtoid.Interprocess.Semaphore.Unix;
using FluentAssertions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Tests
{
    public class SemaphoreUnixTests
    {
        private const string DefaultQueueName = "qn";
        private static readonly string path = Path.GetTempPath();
        private static readonly SharedAssetsIdentifier defaultIdentifier = new SharedAssetsIdentifier(DefaultQueueName, path);

        [Fact(Platforms = Platform.UnixBased)]
        public async Task CanDisposeUnixServer()
        {
            // simple create and dispose
            using (new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger))
            {
            }

            using (var server = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger))
            {
                await server.ReleaseAsync(default);
            }

            using (var server = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger))
            {
                await server.ReleaseAsync(default);
                await Task.Delay(500);
            }
        }

        [Fact(Platforms = Platform.UnixBased)]
        public void CanDisposeUnixClient()
        {
            // simple create and dispose
            using (new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger))
            {
            }

            using (var client = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger))
            {
                client.WaitOne(50).Should().BeFalse();
            }

            using (var client = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger))
            {
                client.WaitOne(500).Should().BeFalse();
            }
        }

        [Fact(Platforms = Platform.UnixBased)]
        public async Task CanSignalMultipleClients()
        {
            using var server = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger);
            using var client1 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);
            using var client2 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);

            await WaitForClientCount(server, 2);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            const int Count = 10000;
            var start = DateTime.Now;
            for (int i = 0; i < Count; i++)
            {
                await server.ReleaseAsync(default);

                client1.WaitOne(1000).Should().BeTrue();
                client2.WaitOne(1000).Should().BeTrue();
            }
            Console.WriteLine("Latency - " + ((DateTime.Now - start).TotalMilliseconds / Count));

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
        }

        [Fact(Platforms = Platform.UnixBased)]
        public async Task CanReceiveSignalsAtDifferentPaces()
        {
            using var server = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger);
            using var client1 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);
            using var client2 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);

            await WaitForClientCount(server, 2);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            const int Count = 10000;
            var start = DateTime.Now;
            for (int i = 0; i < Count; i++)
            {
                await server.ReleaseAsync(default);
                client1.WaitOne(1000).Should().BeTrue();
            }
            Console.WriteLine("Send and receive latency - " + ((DateTime.Now - start).TotalMilliseconds / Count));

            client1.WaitOne(50).Should().BeFalse();

            start = DateTime.Now;
            for (int i = 0; i < Count; i++)
                client2.WaitOne(1000).Should().BeTrue();

            Console.WriteLine("Receive latency - " + ((DateTime.Now - start).TotalMilliseconds / Count));

            client2.WaitOne(50).Should().BeFalse();
        }

        [Fact(Platforms = Platform.UnixBased)]
        public async Task CanAddClientLater()
        {
            using var server = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger);
            using var client1 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);
            using var client2 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);

            await WaitForClientCount(server, 2);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            await server.ReleaseAsync(default);

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            using var client3 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);
            await WaitForClientCount(server, 3);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            await server.ReleaseAsync(default);

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();
        }

        [Fact(Platforms = Platform.UnixBased)]
        public async Task CanSupportManyClients()
        {
            const int Count = 20;

            using var server = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger);
            var clients = new SemaphoreWaiter[Count];

            for (int i = 0; i < Count; i++)
            {
                Console.WriteLine(i);
                clients[i] = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);
            }

            await WaitForClientCount(server, Count);
            await server.ReleaseAsync(default);

            for (int i = 0; i < Count; i++)
                clients[i].Dispose();

            await server.ReleaseAsync(default);
            await WaitForClientCount(server, 0);
        }

        [Fact(Platforms = Platform.UnixBased)]
        public async Task CanSupporrtMultipleServersAndClients()
        {
            using var server1 = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger);
            using var client1 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);
            using var client2 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);

            await WaitForClientCount(server1, 2);

            await server1.ReleaseAsync(default);
            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            using var server2 = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger);
            await WaitForClientCount(server2, 2);

            await server1.ReleaseAsync(default);
            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            await server2.ReleaseAsync(default);
            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            await server1.ReleaseAsync(default);
            await server2.ReleaseAsync(default);
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
        [Fact(Platforms = Platform.UnixBased)]
        public async Task CanPerformManyActions()
        {
            using var server1 = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger);
            using var client1 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);
            using var client2 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);

            await WaitForClientCount(server1, 2);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            var start = DateTime.Now;
            await server1.ReleaseAsync(default);

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            Console.WriteLine("Signal 1 - " + (DateTime.Now - start).TotalMilliseconds);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();

            start = DateTime.Now;
            await server1.ReleaseAsync(default);

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            Console.WriteLine("Signal 2 - " + (DateTime.Now - start).TotalMilliseconds);

            using var client3 = new SemaphoreWaiter(defaultIdentifier, TestUtils.Logger);
            await WaitForClientCount(server1, 3);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            start = DateTime.Now;
            await server1.ReleaseAsync(default);

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();
            Console.WriteLine("Signal 3 - " + (DateTime.Now - start).TotalMilliseconds);

            using var server2 = new SemaphoreReleaser(defaultIdentifier, TestUtils.Logger);
            await WaitForClientCount(server2, 3);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            start = DateTime.Now;
            await server2.ReleaseAsync(default);

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();
            Console.WriteLine("Signal 4 - " + (DateTime.Now - start).TotalMilliseconds);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            start = DateTime.Now;
            for (int i = 0; i < 10000; i++)
            {
                await server1.ReleaseAsync(default);

                client1.WaitOne(1000).Should().BeTrue();
                client2.WaitOne(1000).Should().BeTrue();
                client3.WaitOne(1000).Should().BeTrue();
            }
            Console.WriteLine("Signal 5 (Average) - " + ((DateTime.Now - start).TotalMilliseconds / 10000));

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            start = DateTime.Now;
            await server1.ReleaseAsync(default);
            await server1.ReleaseAsync(default);

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();
            Console.WriteLine("Signal 6 - " + (DateTime.Now - start).TotalMilliseconds);

            start = DateTime.Now;
            await server1.ReleaseAsync(default);
            await server2.ReleaseAsync(default);

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();

            client1.WaitOne(1000).Should().BeTrue();
            client2.WaitOne(1000).Should().BeTrue();
            client3.WaitOne(1000).Should().BeTrue();
            Console.WriteLine("Signal 7 - " + (DateTime.Now - start).TotalMilliseconds);

            client1.WaitOne(50).Should().BeFalse();
            client2.WaitOne(50).Should().BeFalse();
            client3.WaitOne(50).Should().BeFalse();

            Console.WriteLine("Disposing all");
        }

        //        [Fact(Platforms = Platform.UnixBased)] 
        //public async Task UnixSignalTests()
        //{
        //    using (var semaphore = new UnixSemaphore(defaultIdentifier))
        //    {
        //        semaphore.WaitOne(50).Should().BeFalse();

        //        await semaphore.ReleaseAsync(default);

        //        semaphore.WaitOne(5000).Should().BeTrue();
        //        semaphore.WaitOne(50).Should().BeFalse();
        //    }
        //}

        private static async Task WaitForClientCount(SemaphoreReleaser server, int count)
        {
            while (server.ClientCount != count)
                await Task.Delay(5);
        }
    }
}
