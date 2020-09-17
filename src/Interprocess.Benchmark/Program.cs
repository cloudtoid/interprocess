using System.Threading;
using FluentAssertions;

namespace Cloudtoid.Interprocess.Benchmark
{
    public sealed class Program
    {
        public static void Main()
        {
            var message = new byte[] { 1, 2, 3 };
            var messageBuffer = new byte[3];
            CancellationToken cancellationToken = default;

            var factory = new QueueFactory();
            var options = new QueueOptions(
                queueName: "my-queue",
                bytesCapacity: 1024 * 1024,
                createOrOverride: true);

            using (var publisher = factory.CreatePublisher(options))
            {
                publisher.TryEnqueue(message);

                options = new QueueOptions(
                    queueName: "my-queue",
                    bytesCapacity: 1024 * 1024);

                using var subscriber = factory.CreateSubscriber(options);
                subscriber.TryDequeue(messageBuffer, cancellationToken, out var msg);

                msg.ToArray().Should().BeEquivalentTo(message);
            }

            using (var publisher = factory.CreatePublisher(options))
            {
                publisher.TryEnqueue(message);

                options = new QueueOptions(
                    queueName: "my-queue",
                    bytesCapacity: 1024 * 1024);

                using var subscriber = factory.CreateSubscriber(options);
                subscriber.TryDequeue(messageBuffer, cancellationToken, out var msg);

                msg.ToArray().Should().BeEquivalentTo(message);
            }
        }
    }
}
