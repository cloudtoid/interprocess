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
                client.Wait(1).Should().BeFalse();
            }

            using (var client = new UnixSemaphore.Client(defaultIdentifier))
            {
                client.Wait(500).Should().BeFalse();
            }
        }

        [Fact]
        public async Task CanConnectMultipleClientsToMultipleServer()
        {
            using (var server1 = new UnixSemaphore.Server(defaultIdentifier))
            using (var client1 = new UnixSemaphore.Client(defaultIdentifier))
            using (var client2 = new UnixSemaphore.Client(defaultIdentifier))
            {
                // wait a while for the server to start and the clients
                // to connect to the server
                while (server1.ClientCount != 2)
                    await Task.Delay(5);

                client1.Wait(10).Should().BeFalse();
                client2.Wait(10).Should().BeFalse();

                var start = DateTime.Now;
                await server1.SignalAsync(default);

                client1.Wait(1000).Should().BeTrue();
                client2.Wait(1000).Should().BeTrue();
                Console.WriteLine("Signal 1 - " + (DateTime.Now - start).TotalMilliseconds);

                client1.Wait(10).Should().BeFalse();
                client2.Wait(10).Should().BeFalse();

                start = DateTime.Now;
                await server1.SignalAsync(default);

                client1.Wait(1000).Should().BeTrue();
                Console.WriteLine("Signal 2(1) - " + (DateTime.Now - start).TotalMilliseconds);
                client2.Wait(1000).Should().BeTrue();
                Console.WriteLine("Signal 2 - " + (DateTime.Now - start).TotalMilliseconds);

                using (var client3 = new UnixSemaphore.Client(defaultIdentifier))
                {
                    while (server1.ClientCount != 3)
                        await Task.Delay(5);

                    client1.Wait(10).Should().BeFalse();
                    client2.Wait(10).Should().BeFalse();
                    client3.Wait(10).Should().BeFalse();

                    start = DateTime.Now;
                    await server1.SignalAsync(default);

                    client1.Wait(1000).Should().BeTrue();
                    client2.Wait(1000).Should().BeTrue();
                    client3.Wait(1000).Should().BeTrue();
                    Console.WriteLine("Signal 3 - " + (DateTime.Now - start).TotalMilliseconds);

                    using (var server2 = new UnixSemaphore.Server(defaultIdentifier))
                    {
                        while (server2.ClientCount != 3)
                            await Task.Delay(5);

                        client1.Wait(10).Should().BeFalse();
                        client2.Wait(10).Should().BeFalse();
                        client3.Wait(10).Should().BeFalse();

                        start = DateTime.Now;
                        await server2.SignalAsync(default);

                        client1.Wait(1000).Should().BeTrue();
                        client2.Wait(1000).Should().BeTrue();
                        client3.Wait(1000).Should().BeTrue();
                        Console.WriteLine("Signal 4 - " + (DateTime.Now - start).TotalMilliseconds);

                        client1.Wait(10).Should().BeFalse();
                        client2.Wait(10).Should().BeFalse();
                        client3.Wait(10).Should().BeFalse();

                        start = DateTime.Now;
                        await server1.SignalAsync(default);

                        client1.Wait(1000).Should().BeTrue();
                        client2.Wait(1000).Should().BeTrue();
                        client3.Wait(1000).Should().BeTrue();
                        Console.WriteLine("Signal 5 - " + (DateTime.Now - start).TotalMilliseconds);

                        client1.Wait(10).Should().BeFalse();
                        client2.Wait(10).Should().BeFalse();
                        client3.Wait(10).Should().BeFalse();

                        start = DateTime.Now;
                        await server1.SignalAsync(default);
                        await server2.SignalAsync(default);

                        client1.Wait(1000).Should().BeTrue("1");
                        client2.Wait(1000).Should().BeTrue("2");
                        client3.Wait(1000).Should().BeTrue("3");

                        client1.Wait(1000).Should().BeTrue("4");
                        client2.Wait(1000).Should().BeTrue("5");
                        client3.Wait(1000).Should().BeTrue("6");
                        Console.WriteLine("Signal 6 - " + (DateTime.Now - start).TotalMilliseconds);

                        client1.Wait(10).Should().BeFalse();
                        client2.Wait(10).Should().BeFalse();
                        client3.Wait(10).Should().BeFalse();

                        Console.WriteLine("Disposing all");
                    }
                }
            }
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
    }
}
