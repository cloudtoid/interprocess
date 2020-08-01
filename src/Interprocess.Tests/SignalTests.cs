using FluentAssertions;
using System;
using Xunit;
using static Cloudtoid.Interprocess.InteprocessSignal;

namespace Cloudtoid.Interprocess.Tests
{
    public class SignalTests
    {
        private const string DefaultQueueName = "queue-name";

        [Fact]
        public void LinuxSignalTests()
        {
            using(var signal = new LinuxSignal(DefaultQueueName, Environment.CurrentDirectory))
            {
                signal.Wait(1).Should().BeTrue();
                signal.Wait(1).Should().BeFalse();

                signal.Signal();
                signal.Wait(100).Should().BeTrue();
                signal.Wait(1).Should().BeFalse();
            }
        }
    }
}
