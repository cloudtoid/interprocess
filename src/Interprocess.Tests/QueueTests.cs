using FluentAssertions;
using System;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class QueueTests
    {
        private const string DefaultQueueName = "queue-name";

        [Fact]
        public void CanEnqueueAndDequeue()
        {
            var value = new byte[] { 100, 110, 120 };

            using (var p = InterprocessQueue.CreatePublisher(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: true)))
            using (var c = InterprocessQueue.CreateSubscriber(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: false)))
            {
                p.TryEnqueue(value).Should().BeTrue();
                c.TryDequeue(default, out var message).Should().BeTrue();
                message.ToArray().Should().BeEquivalentTo(value);

                p.TryEnqueue(value).Should().BeTrue();
                c.TryDequeue(default, out message).Should().BeTrue();
                message.ToArray().Should().BeEquivalentTo(value);
            }
        }

        [Fact]
        public void CannotEnqueuePastCapacity()
        {
            using (var p = InterprocessQueue.CreatePublisher(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: true)))
            {
                p.TryEnqueue(new byte[] { 100, 110, 120 }).Should().BeTrue();
                p.TryEnqueue(new byte[] { 140 }).Should().BeFalse();
            }
        }

        [Fact]
        public void DisposeShouldNotThrowFileNotFoundException()
        {
            var p = InterprocessQueue.CreatePublisher(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: true));
            p.TryEnqueue(new byte[] { 100, 110, 120 }).Should().BeTrue();
            using (var c = InterprocessQueue.CreateSubscriber(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: false)))
            {
                p.Dispose();
            }
        }
    }
}
