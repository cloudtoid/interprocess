using Cloudtoid.Interprocess.Signal.Unix;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class SignalTests
    {
        private const string DefaultQueueName = "queue-name";
        private static readonly string path = string.Empty;

        [Fact]
        public async Task CanDisposeUnixServer()
        {
            // simple create and dispose
            using (var server = new UnixSignal.Server(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
            }

            using (var server = new UnixSignal.Server(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                await server.SignalAsync();
            }

            using (var server = new UnixSignal.Server(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                await server.SignalAsync();
                await Task.Delay(500);
            }
        }

        [Fact]
        public void CanDisposeUnixClient()
        {
            // simple create and dispose
            using (var server = new UnixSignal.Client(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
            }

            using (var server = new UnixSignal.Client(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                server.Wait(1).Should().BeFalse();
            }

            using (var server = new UnixSignal.Client(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                server.Wait(500).Should().BeFalse();
            }
        }

        [Fact]
        public async Task UnixSignalTests()
        {
            using (var signal = new UnixSignal(new SharedAssetsIdentifier(DefaultQueueName, path)))
            {
                signal.Wait(1).Should().BeTrue();
                signal.Wait(1).Should().BeFalse();

                await signal.SignalAsync();

                signal.Wait(100).Should().BeTrue();
                signal.Wait(1).Should().BeFalse();
            }
        }
    }
}
