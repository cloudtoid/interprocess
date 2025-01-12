using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Cloudtoid.Interprocess.Tests;

public class QueueTests : IClassFixture<UniquePathFixture>
{
    private static readonly byte[] ByteArray1 = [100,];
    private static readonly byte[] ByteArray2 = [100, 110];
    private static readonly byte[] ByteArray3 = [100, 110, 120];
    private static readonly byte[] ByteArray50 = Enumerable.Range(1, 50).Select(i => (byte)i).ToArray();
    private readonly UniquePathFixture fixture;
    private readonly QueueFactory queueFactory;

    public QueueTests(
        UniquePathFixture fixture,
        ITestOutputHelper testOutputHelper)
    {
        this.fixture = fixture;
#pragma warning disable CA2000 // Dispose objects before losing scope
        var loggerFactory = new LoggerFactory();
        loggerFactory.AddProvider(new XunitLoggerProvider(testOutputHelper));
#pragma warning restore CA2000 // Dispose objects before losing scope
        queueFactory = new QueueFactory(loggerFactory);
    }

    [Fact]
    [TestBeforeAfter]
    public void Sample()
    {
        var message = new byte[] { 1, 2, 3 };
        var messageBuffer = new byte[3];
        CancellationToken cancellationToken = default;

        var factory = new QueueFactory();
        var options = new QueueOptions(
            queueName: "my-queue",
            capacity: 1024 * 1024);

        using var publisher = factory.CreatePublisher(options);
        publisher.TryEnqueue(message);

        options = new QueueOptions(
            queueName: "my-queue",
            capacity: 1024 * 1024);

        using var subscriber = factory.CreateSubscriber(options);
        subscriber.TryDequeue(messageBuffer, cancellationToken, out var msg);

        msg.ToArray().Should().BeEquivalentTo(message);
    }

    [Fact]
    [TestBeforeAfter]
    public void DependencyInjectionSample()
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
            capacity: 1024 * 1024);

        using var publisher = factory.CreatePublisher(options);
        publisher.TryEnqueue(message);

        options = new QueueOptions(
            queueName: "my-queue",
            capacity: 1024 * 1024);

        using var subscriber = factory.CreateSubscriber(options);
        subscriber.TryDequeue(messageBuffer, cancellationToken, out var msg);

        msg.ToArray().Should().BeEquivalentTo(message);
    }

    [Fact]
    [TestBeforeAfter]
    public void CanEnqueueAndDequeue()
    {
        using var p = CreatePublisher(24);
        using var s = CreateSubscriber(24);

        p.TryEnqueue(ByteArray3).Should().BeTrue();
        var message = s.Dequeue(default);
        message.ToArray().Should().BeEquivalentTo(ByteArray3);

        p.TryEnqueue(ByteArray3).Should().BeTrue();
        message = s.Dequeue(default);
        message.ToArray().Should().BeEquivalentTo(ByteArray3);

        p.TryEnqueue(ByteArray2).Should().BeTrue();
        message = s.Dequeue(default);
        message.ToArray().Should().BeEquivalentTo(ByteArray2);

        p.TryEnqueue(ByteArray2).Should().BeTrue();
        message = s.Dequeue(new byte[5], default);
        message.ToArray().Should().BeEquivalentTo(ByteArray2);
    }

    [Fact]
    [TestBeforeAfter]
    public void CanEnqueueDequeueWrappedMessage()
    {
        using var p = CreatePublisher(128);
        using var s = CreateSubscriber(128);

        p.TryEnqueue(ByteArray50).Should().BeTrue();
        var message = s.Dequeue(default);
        message.ToArray().Should().BeEquivalentTo(ByteArray50);

        p.TryEnqueue(ByteArray50).Should().BeTrue();
        message = s.Dequeue(default);
        message.ToArray().Should().BeEquivalentTo(ByteArray50);

        p.TryEnqueue(ByteArray50).Should().BeTrue();
        message = s.Dequeue(default);
        message.ToArray().Should().BeEquivalentTo(ByteArray50);

        p.TryEnqueue(ByteArray50).Should().BeTrue();
        message = s.Dequeue(default);
        message.ToArray().Should().BeEquivalentTo(ByteArray50);
    }

    [Fact]
    [TestBeforeAfter]
    public void CannotEnqueuePastCapacity()
    {
        using var p = CreatePublisher(24);

        p.TryEnqueue(ByteArray3).Should().BeTrue();
        p.TryEnqueue(ByteArray1).Should().BeFalse();
    }

    [Fact]
    [TestBeforeAfter]
    public void DisposeShouldNotThrow()
    {
        var p = CreatePublisher(24);
        p.TryEnqueue(ByteArray3).Should().BeTrue();

        using var s = CreateSubscriber(24);
        p.Dispose();

        s.Dequeue(default);
    }

    [Fact]
    [TestBeforeAfter]
    public void CannotReadAfterProducerIsDisposed()
    {
        var p = CreatePublisher(24);
        p.TryEnqueue(ByteArray3).Should().BeTrue();
        using (var s = CreateSubscriber(24))
            p.Dispose();

        using (CreatePublisher(24))
        using (var s = CreateSubscriber(24))
            s.TryDequeue(default, out var message).Should().BeFalse();
    }

    [Theory]
    [Repeat(10)]
    [TestBeforeAfter]
#pragma warning disable RCS1163 // Unused parameter
#pragma warning disable IDE0060 // Remove unused parameter
#pragma warning disable xUnit1026 // Theory methods should use all of their parameters
    public async Task CanDisposeQueueAsync(int i)
#pragma warning restore xUnit1026 // Theory methods should use all of their parameters
#pragma warning restore IDE0060 // Remove unused parameter
#pragma warning restore RCS1163 // Unused parameter
    {
        using var s = CreateSubscriber(1024);
        _ = Task.Run(() => s.Dequeue(default));
        await Task.Delay(200);
    }

    [Fact]
    [TestBeforeAfter]
    public void CanCircleBuffer()
    {
        using var p = CreatePublisher(1024);
        using var s = CreateSubscriber(1024);

        var message = Enumerable.Range(100, 66).Select(i => (byte)i).ToArray();

        for (var i = 0; i < 20000; i++)
        {
            p.TryEnqueue(message).Should().BeTrue();
            var result = s.Dequeue(default);
            result.ToArray().Should().BeEquivalentTo(message);
        }
    }

    [Fact]
    [TestBeforeAfter]
    public void CanRejectLargeMessages()
    {
        using (var p = CreatePublisher(24))
        using (var s = CreateSubscriber(24))
        {
            p.TryEnqueue(ByteArray3).Should().BeTrue();
            var message = s.Dequeue(default);
            message.ToArray().Should().BeEquivalentTo(ByteArray3);

            p.TryEnqueue(ByteArray3).Should().BeTrue();

            // This should fail because the queue is out of capacity
            p.TryEnqueue(ByteArray3).Should().BeFalse();

            message = s.Dequeue(default);
            message.ToArray().Should().BeEquivalentTo(ByteArray3);

            p.TryEnqueue(ByteArray3).Should().BeTrue();
            p.TryEnqueue(ByteArray3).Should().BeFalse();
        }

        using (var p = CreatePublisher(32))
        {
            p.TryEnqueue(ByteArray3).Should().BeTrue();
            p.TryEnqueue(ByteArray3).Should().BeTrue();
            p.TryEnqueue(ByteArray3).Should().BeFalse();
        }

        using (var p = CreatePublisher(32))
            p.TryEnqueue(ByteArray50).Should().BeFalse(); // failed here
    }

    [Fact]
    [TestBeforeAfter]
    public void CanRecoverIfPublisherCrashes()
    {
        // This is very complicated test that is trying to replicate a crash scenario when the publisher
        // crashes after indicating that it is writing the message but before completing the operation.

        using var dp = new DeadlockCausingPublisher(new("qn", fixture.Path, 1024), NullLoggerFactory.Instance);
        dp.TryEnqueue(ByteArray3).Should().BeTrue();

        using var p = CreatePublisher(1024);
        p.TryEnqueue(ByteArray1).Should().BeTrue();
        using var s = CreateSubscriber(1024);

        // This line should take 10 seconds to return (that is how long the timeout is set in teh code)
        // After the 10 seconds expires, we should have lost all other messages that were in teh queue when we started the dequeue process.
        s.TryDequeue(default, out _).Should().BeFalse();

        // But then, after this 10 seconds delay, system should fully recover and continue with new messages
        p.TryEnqueue(ByteArray1).Should().BeTrue();
        s.TryDequeue(default, out var message).Should().BeTrue();
        message.ToArray().Should().BeEquivalentTo(ByteArray1);
    }

    private IPublisher CreatePublisher(long capacity) =>
        queueFactory.CreatePublisher(new("qn", fixture.Path, capacity));

    private ISubscriber CreateSubscriber(long capacity) =>
        queueFactory.CreateSubscriber(new("qn", fixture.Path, capacity));

    private sealed class DeadlockCausingPublisher(QueueOptions options, ILoggerFactory loggerFactory) :
        Queue(options, loggerFactory),
        IPublisher
    {
        public unsafe bool TryEnqueue(ReadOnlySpan<byte> message)
        {
            var bodyLength = message.Length;
            var messageLength = GetPaddedMessageLength(bodyLength);
            var header = *Header;
            Header->WriteOffset = SafeIncrementMessageOffset(header.WriteOffset, messageLength);
            return true;
        }
    }
}