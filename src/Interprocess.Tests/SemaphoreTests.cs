using Cloudtoid.Interprocess.Semaphore.Unix;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class SemaphoreTests
    {
        private const string DefaultQueueName = "queue-name";
        private static readonly string path = string.Empty;

        [Fact]
        public async Task CanDisposeUnixServer()
        {
            // simple create and dispose
            using (var server = new UnixSemaphore.Server(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
            }

            using (var server = new UnixSemaphore.Server(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                await server.SignalAsync();
            }

            using (var server = new UnixSemaphore.Server(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                await server.SignalAsync();
                await Task.Delay(500);
            }
        }

        [Fact]
        public void CanDisposeUnixClient()
        {
            // simple create and dispose
            using (var server = new UnixSemaphore.Client(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
            }

            using (var server = new UnixSemaphore.Client(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                server.Wait(1).Should().BeFalse();
            }

            using (var server = new UnixSemaphore.Client(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                server.Wait(500).Should().BeFalse();
            }
        }

        [Fact]
        public async Task UnixSignalTests()
        {
            using (var semaphore = new UnixSemaphore(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                semaphore.WaitOne(1).Should().BeTrue();
                semaphore.WaitOne(1).Should().BeFalse();

                await semaphore.ReleaseAsync();

                semaphore.WaitOne(100).Should().BeTrue();
                semaphore.WaitOne(1).Should().BeFalse();
            }
        }
    }
}
