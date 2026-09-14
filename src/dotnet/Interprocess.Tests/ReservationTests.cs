namespace Cloudtoid.Interprocess.Tests;

public sealed class ReservationTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    [Theory]
    [InlineData(64)]
    [InlineData(120)]
    public unsafe void WritePositionIsNotReusedAfterTwoBufferLaps(int capacity)
    {
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, capacity);
        var factory = new QueueFactory();
        using var publisher = new Publisher(options, NullLoggerFactory.Instance);
        using var subscriber = factory.CreateSubscriber(options);
        var staleWriteOffset = publisher.Header->WriteOffset;
        var payload = new byte[capacity - 8];

        // A publisher paused before its reservation must not see the same write position after two laps.
        publisher.TryEnqueue(payload).Should().BeTrue();
        subscriber.TryDequeue(out _).Should().BeTrue();
        publisher.TryEnqueue(payload).Should().BeTrue();
        publisher.Header->ReadOffset.Should().Be(capacity);
        publisher.Header->WriteOffset.Should().Be(2L * capacity);
        publisher.Header->WriteOffset.Should().NotBe(staleWriteOffset);
        publisher.TryEnqueue([]).Should().BeFalse("the second lap still occupies the entire buffer");
        subscriber.TryDequeue(out var message).Should().BeTrue();
        message.ToArray().Should().Equal(payload);
        subscriber.TryDequeue(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(64, 0)]
    [InlineData(64, 8)]
    [InlineData(64, 24)]
    [InlineData(120, 0)]
    [InlineData(120, 8)]
    [InlineData(120, 24)]
    public unsafe void CounterExhaustionPreservesAcceptedMessages(int capacity, int bodyLength)
    {
        const long lastPosition = long.MaxValue & ~7L;
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, capacity);
        var factory = new QueueFactory();
        using var publisher = new Publisher(options, NullLoggerFactory.Instance);
        using var subscriber = factory.CreateSubscriber(options);
        using var signal = InterprocessSemaphore.CreateWaiter(options.QueueName);
        var payload = Enumerable.Range(0, bodyLength).Select(value => (byte)value).ToArray();
        var start = lastPosition - bodyLength - 8;
        publisher.Header->ReadOffset = start;
        publisher.Header->WriteOffset = start;

        Assert.Throws<OverflowException>(() => publisher.TryEnqueue(new byte[bodyLength + 8]));
        publisher.Header->WriteOffset.Should().Be(start);
        signal.Wait(0).Should().BeFalse();

        publisher.TryEnqueue(payload).Should().BeTrue();
        publisher.Header->WriteOffset.Should().Be(lastPosition);
        signal.Wait(0).Should().BeTrue();
        Assert.Throws<OverflowException>(() => publisher.TryEnqueue([]));
        publisher.Header->ReadOffset.Should().Be(start);
        publisher.Header->WriteOffset.Should().Be(lastPosition);
        signal.Wait(0).Should().BeFalse("an overflowing reservation must not signal or change the queue");

        subscriber.TryDequeue(out var message).Should().BeTrue();
        message.ToArray().Should().Equal(payload);
        publisher.Header->ReadOffset.Should().Be(lastPosition);
        subscriber.TryDequeue(out _).Should().BeFalse();
        Assert.Throws<OverflowException>(() => publisher.TryEnqueue([]));
        publisher.Header->WriteOffset.Should().Be(lastPosition, "empty queues must not reset their positions");
    }

    [Theory]
    [InlineData(32, 16)]
    [InlineData(0, 80)]
    public unsafe void InconsistentPositionsCannotGrantCapacity(long readOffset, long writeOffset)
    {
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 64);
        using var publisher = new Publisher(options, NullLoggerFactory.Instance);
        publisher.Header->ReadOffset = readOffset;
        publisher.Header->WriteOffset = writeOffset;

        publisher.TryEnqueue([]).Should().BeFalse();
        publisher.Header->ReadOffset.Should().Be(readOffset);
        publisher.Header->WriteOffset.Should().Be(writeOffset);
    }
}