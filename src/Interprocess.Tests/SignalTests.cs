using Cloudtoid.Interprocess.Signal.Unix;
using FluentAssertions;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class SignalTests
    {
        private const string DefaultQueueName = "queue-name";
        private static readonly string path = string.Empty;

        [Fact]
        public async Task UnixDomainSocketServerTests()
        {
            // simple create and dispose
            using (var server = new Server(DefaultQueueName, path))
            {
            }

            using (var server = new Server(DefaultQueueName, path))
            {
                await server.SignalAsync();
            }

            using (var server = new Server(DefaultQueueName, path))
            {
                await server.SignalAsync();
                await Task.Delay(500);
            }
        }

        [Fact]
        public async Task UnixSignalTests()
        {
            using (var signal = new UnixSignal(DefaultQueueName, path))
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
