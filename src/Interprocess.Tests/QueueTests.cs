using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class QueueTests
    {
        private const string DefaultQueueName = "queue-name";
        private static readonly byte[] byteArray1 = new byte[] { 100, };
        private static readonly byte[] byteArray2 = new byte[] { 100, 110 };
        private static readonly byte[] byteArray3 = new byte[] { 100, 110, 120 };

        [Fact]
        public async Task CanEnqueueAndDequeue()
        {
            using (var p = InterprocessQueue.CreatePublisher(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: true)))
            using (var c = InterprocessQueue.CreateSubscriber(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: false)))
            {
                p.TryEnqueue(byteArray3).Should().BeTrue();
                var message = await c.WaitDequeueAsync(default);
                message.ToArray().Should().BeEquivalentTo(byteArray3);

                p.TryEnqueue(byteArray3).Should().BeTrue();
                message = await c.WaitDequeueAsync(default);
                message.ToArray().Should().BeEquivalentTo(byteArray3);
            }
        }

        [Fact]
        public void CannotEnqueuePastCapacity()
        {
            using (var p = InterprocessQueue.CreatePublisher(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: true)))
            {
                p.TryEnqueue(byteArray3).Should().BeTrue();
                p.TryEnqueue(byteArray1).Should().BeFalse();
            }
        }

        [Fact]
        public async Task DisposeShouldNotThrow()
        {
            var p = InterprocessQueue.CreatePublisher(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: true));
            p.TryEnqueue(byteArray3).Should().BeTrue();
            using (var c = InterprocessQueue.CreateSubscriber(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: false)))
            {
                p.Dispose();

                // The memory mapped file should not have been deleted so this line should work just fine
                var message = await c.WaitDequeueAsync(default);
                message.ToArray().Should().BeEquivalentTo(byteArray3);
            }
        }

        [Fact]
        public async Task ProducerDisposeWithSubscriberKeepsFile()
        {
            var p = InterprocessQueue.CreatePublisher(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: true));
            p.TryEnqueue(new byte[] { 100, 110, 120 }).Should().BeTrue();
            using (var c = InterprocessQueue.CreateSubscriber(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: false)))
            {
                p.Dispose();
            }

            using (InterprocessQueue.CreatePublisher(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: false)))
            using (var c = InterprocessQueue.CreateSubscriber(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, 24, createOrOverride: false)))
            {
                (await c.TryDequeueAsync(default, out var _)).Should().BeFalse();
            }
        }
    }
}
