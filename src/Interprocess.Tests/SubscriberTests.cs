namespace Cloudtoid.Interprocess.Tests;

public sealed class SubscriberTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    private readonly QueueOptions options = new(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 256);
    private readonly QueueFactory factory = new();

    [Fact]
    public async Task TryDequeueDoesNotWaitForAnotherSubscriberAsync()
    {
        using var probe = new QueueProbe(options);
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        publisher.TryEnqueue("message!"u8).Should().BeTrue();
        probe.LockReads();
        try
        {
            var attempt = Task.Run(() => subscriber.TryDequeue(default, out _));
            (await attempt.WaitAsync(TimeSpan.FromSeconds(1))).Should().BeFalse();
        }
        finally
        {
            probe.UnlockReads();
        }

        subscriber.TryDequeue(default, out _).Should().BeTrue();
    }

    [Fact]
    public void ExpiredSubscriberLockCanBeRecovered()
    {
        using var probe = new QueueProbe(options);
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        publisher.TryEnqueue("message!"u8).Should().BeTrue();
        probe.AbandonReadLock();
        subscriber.TryDequeue(default, out var message).Should().BeTrue();
        message.ToArray().Should().Equal("message!"u8.ToArray());
        probe.ReadsAreLocked.Should().BeFalse();
    }

    [Fact]
    public async Task BlockingDequeueRetriesContendedReadAsync()
    {
        using var probe = new QueueProbe(options);
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        publisher.TryEnqueue("message!"u8).Should().BeTrue();
        probe.LockReads();
        var read = Task.Run(() => subscriber.Dequeue(cancellation.Token));
        try
        {
            await Task.Delay(20);
            read.IsCompleted.Should().BeFalse();
        }
        finally
        {
            probe.UnlockReads();
        }

        (await read.WaitAsync(TimeSpan.FromSeconds(1))).ToArray().Should().Equal("message!"u8.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationInterruptsUnfinishedMessageAsync(bool blocking)
    {
        using var probe = new QueueProbe(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var cancellation = new CancellationTokenSource();
        probe.ReserveUnfinishedMessage();
        var read = Task.Run(() =>
        {
            if (blocking)
                subscriber.Dequeue(cancellation.Token);
            else
                subscriber.TryDequeue(cancellation.Token, out _);
        });

        try
        {
            SpinWait.SpinUntil(() => probe.ReadsAreLocked, TimeSpan.FromSeconds(1)).Should().BeTrue();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await read.WaitAsync(TimeSpan.FromSeconds(1)));
            probe.ReadsAreLocked.Should().BeFalse();
        }
        finally
        {
            await cancellation.CancelAsync();
        }
    }

    [Fact]
    public async Task EmptyBlockingDequeueCanBeCancelledAsync()
    {
        using var subscriber = factory.CreateSubscriber(options);
        using var cancellation = new CancellationTokenSource();
        var read = Task.Run(() => subscriber.Dequeue(cancellation.Token));
        await Task.Delay(20);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await read.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task ConcurrentSubscribersReceiveEveryMessageExactlyOnceAsync(int subscriberCount)
    {
        const int count = 4000;
        var received = new int[count];
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Keep a participant alive while the workers start and finish.
        using var anchor = factory.CreatePublisher(options);
        var readers = new Task[subscriberCount];
        for (var reader = 0; reader < subscriberCount; reader++)
        {
            readers[reader] = Task.Run(() =>
            {
                using var subscriber = factory.CreateSubscriber(options);
                var buffer = new byte[8];
                for (var i = 0; i < count / subscriberCount; i++)
                {
                    var message = subscriber.Dequeue(buffer, cancellation.Token);
                    message.Length.Should().Be(8);
                    var id = BitConverter.ToInt32(message.Span);
                    id.Should().BeInRange(0, count - 1);
                    Interlocked.Increment(ref received[id]);
                }
            });
        }

        var writers = new Task[2];
        for (var writer = 0; writer < writers.Length; writer++)
        {
            var firstId = writer;
            writers[writer] = Task.Run(() =>
            {
                using var publisher = factory.CreatePublisher(options);
                var buffer = new byte[8];
                for (var id = firstId; id < count; id += 2)
                {
                    BitConverter.TryWriteBytes(buffer, id).Should().BeTrue();
                    while (!publisher.TryEnqueue(buffer))
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        Thread.Yield();
                    }
                }
            });
        }

        await Task.WhenAll(readers.Concat(writers)).WaitAsync(TimeSpan.FromSeconds(15));
        received.Should().OnlyContain(value => value == 1);
    }

    private sealed class QueueProbe(QueueOptions options) : Queue(options, NullLoggerFactory.Instance)
    {
        internal unsafe bool ReadsAreLocked => Interlocked.Read(ref Header->ReadLockTimestamp) != 0;

        internal unsafe void LockReads() => Interlocked.Exchange(ref Header->ReadLockTimestamp, DateTime.UtcNow.Ticks);

        internal unsafe void AbandonReadLock() =>
            Interlocked.Exchange(ref Header->ReadLockTimestamp, DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(11).Ticks);

        internal unsafe void UnlockReads() => Interlocked.Exchange(ref Header->ReadLockTimestamp, 0);

        internal unsafe void ReserveUnfinishedMessage() => Interlocked.Exchange(ref Header->WriteOffset, 16);
    }
}