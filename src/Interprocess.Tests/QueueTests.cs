using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Cloudtoid.Interprocess.Tests
{
    public class QueueTests : IClassFixture<UniqueIdentifierFixture>
    {
        private static readonly byte[] ByteArray1 = new byte[] { 100, };
        private static readonly byte[] ByteArray2 = new byte[] { 100, 110 };
        private static readonly byte[] ByteArray3 = new byte[] { 100, 110, 120 };
        private static readonly byte[] ByteArray50 = Enumerable.Range(1, 50).Select(i => (byte)i).ToArray();
        private readonly UniqueIdentifierFixture fixture;
        private readonly QueueFactory queueFactory;

        public QueueTests(
            UniqueIdentifierFixture fixture,
            ITestOutputHelper testOutputHelper)
        {
            this.fixture = fixture;
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new XunitLoggerProvider(testOutputHelper));
            queueFactory = new QueueFactory(loggerFactory);
        }

        [Fact]
        public async Task SampleAsync()
        {
            var message = new byte[] { 1, 2, 3 };
            var messageBuffer = new byte[3];
            CancellationToken cancellationToken = default;

            var factory = new QueueFactory();
            var options = new QueueOptions(
                queueName: "my-queue",
                bytesCapacity: 1024 * 1024,
                createOrOverride: true);

            using var publisher = factory.CreatePublisher(options);
            publisher.TryEnqueue(message);

            options = new QueueOptions(
                queueName: "my-queue",
                bytesCapacity: 1024 * 1024);

            using var subscriber = factory.CreateSubscriber(options);
            await subscriber.TryDequeueAsync(messageBuffer, cancellationToken, out var msg);

            msg.ToArray().Should().BeEquivalentTo(message);
        }

        [Fact]
        public async Task DependencyInjectionSampleAsync()
        {
            var message = new byte[] { 1, 2, 3 };
            var messageBuffer = new byte[3];
            CancellationToken cancellationToken = default;
            var services = new ServiceCollection();

            services
                .AddInterprocessQueue() // adding the queue related components
                .AddLogging(); // optionally, we can enable logging

            var serviceProvider = services.BuildServiceProvider();
            var factory = serviceProvider.GetRequiredService<IQueueFactory>();

            var options = new QueueOptions(
                queueName: "my-queue",
                bytesCapacity: 1024 * 1024,
                createOrOverride: true);

            using var publisher = factory.CreatePublisher(options);
            publisher.TryEnqueue(message);

            options = new QueueOptions(
                queueName: "my-queue",
                bytesCapacity: 1024 * 1024);

            using var subscriber = factory.CreateSubscriber(options);
            await subscriber.TryDequeueAsync(messageBuffer, cancellationToken, out var msg);

            msg.ToArray().Should().BeEquivalentTo(message);
        }

        [Fact]
        public async Task CanEnqueueAndDequeueAsync()
        {
            using var p = CreatePublisher(24, createOrOverride: true);
            using var s = CreateSubscriber(24);

            p.TryEnqueue(ByteArray3).Should().BeTrue();
            var message = await s.DequeueAsync(default);
            message.ToArray().Should().BeEquivalentTo(ByteArray3);

            p.TryEnqueue(ByteArray3).Should().BeTrue();
            message = await s.DequeueAsync(default);
            message.ToArray().Should().BeEquivalentTo(ByteArray3);

            p.TryEnqueue(ByteArray2).Should().BeTrue();
            message = await s.DequeueAsync(default);
            message.ToArray().Should().BeEquivalentTo(ByteArray2);

            p.TryEnqueue(ByteArray2).Should().BeTrue();
            message = await s.DequeueAsync(new byte[5], default);
            message.ToArray().Should().BeEquivalentTo(ByteArray2);
        }

        [Fact]
        public async Task CanEnqueueDequeueWrappedMessageAsync()
        {
            using var p = CreatePublisher(128, createOrOverride: true);
            using var s = CreateSubscriber(128);

            p.TryEnqueue(ByteArray50).Should().BeTrue();
            var message = await s.DequeueAsync(default);
            message.ToArray().Should().BeEquivalentTo(ByteArray50);

            p.TryEnqueue(ByteArray50).Should().BeTrue();
            message = await s.DequeueAsync(default);
            message.ToArray().Should().BeEquivalentTo(ByteArray50);

            p.TryEnqueue(ByteArray50).Should().BeTrue();
            message = await s.DequeueAsync(default);
            message.ToArray().Should().BeEquivalentTo(ByteArray50);
        }

        [Fact]
        public void CannotEnqueuePastCapacity()
        {
            using var p = CreatePublisher(24, createOrOverride: true);

            p.TryEnqueue(ByteArray3).Should().BeTrue();
            p.TryEnqueue(ByteArray1).Should().BeFalse();
        }

        [Fact]
        public async Task DisposeShouldNotThrowAsync()
        {
            var p = CreatePublisher(24, createOrOverride: true);
            p.TryEnqueue(ByteArray3).Should().BeTrue();

            using var s = CreateSubscriber(24);
            p.Dispose();

            await s.DequeueAsync(default);
        }

        [Fact]
        public async Task CannotReadAfterProducerIsDisposedAsync()
        {
            var p = CreatePublisher(24, createOrOverride: true);
            p.TryEnqueue(ByteArray3).Should().BeTrue();
            using (var s = CreateSubscriber(24))
                p.Dispose();

            using (CreatePublisher(24))
            using (var s = CreateSubscriber(24))
            {
                (await s.TryDequeueAsync(default, out var message)).Should().BeFalse();
            }
        }

        private IPublisher CreatePublisher(long capacity, bool createOrOverride = false)
            => queueFactory.CreatePublisher(
                new QueueOptions(fixture.Identifier.Name, fixture.Identifier.Path, capacity, createOrOverride));

        private ISubscriber CreateSubscriber(long capacity, bool createOrOverride = false)
            => queueFactory.CreateSubscriber(
                new QueueOptions(fixture.Identifier.Name, fixture.Identifier.Path, capacity, createOrOverride));
    }
}
