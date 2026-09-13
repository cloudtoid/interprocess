using System.Buffers;

namespace Cloudtoid.Interprocess.Tests;

public sealed class SubscriberTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    private readonly QueueOptions options = new(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 256);
    private readonly QueueFactory factory = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 8)]
    [InlineData(50, 0)]
    [InlineData(50, 7)]
    [InlineData(50, 50)]
    [InlineData(50, 80)]
    public void ReusedBuffersPreserveEmptyTruncatedAndWrappedMessages(int messageLength, int bufferLength)
    {
        var wrappedOptions = new QueueOptions(options.QueueName, options.Path, 120);
        using var publisher = factory.CreatePublisher(wrappedOptions);
        using var subscriber = factory.CreateSubscriber(wrappedOptions);
        var payload = new byte[messageLength];
        var destination = new byte[bufferLength];
        for (var i = 0; i < 8; i++)
        {
            payload.AsSpan().Fill((byte)i);
            publisher.TryEnqueue(payload).Should().BeTrue();
            subscriber.TryDequeue(destination, default, out var message).Should().BeTrue();
            message.ToArray().Should().Equal(payload.Take(bufferLength));
        }
    }

    [Fact]
    public void DestinationFailureLeavesMessagesAvailableForRetry()
    {
        using var probe = new QueueProbe(options);
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var destination = new TestMemory();
        var memory = destination.Memory;
        destination.FailReads = true;
        publisher.TryEnqueue("original"u8).Should().BeTrue();
        publisher.TryEnqueue("next-msg"u8).Should().BeTrue();

        for (var i = 0; i < 2; i++)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            Assert.Throws<InvalidOperationException>(() => subscriber.TryDequeue(memory, cancellation.Token, out _));
            probe.ReadsAreLocked.Should().BeFalse();
        }

        using var retryCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        subscriber.TryDequeue(retryCancellation.Token, out var first).Should().BeTrue();
        first.ToArray().Should().Equal("original"u8.ToArray());
        subscriber.TryDequeue(retryCancellation.Token, out var second).Should().BeTrue();
        second.ToArray().Should().Equal("next-msg"u8.ToArray());
    }

    [Fact]
    public void DestinationDoesNotNeedToSupportPinning()
    {
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var destination = new TestMemory { SupportsPinning = false };
        publisher.TryEnqueue("message!"u8).Should().BeTrue();

        subscriber.TryDequeue(destination.Memory, default, out var message).Should().BeTrue();
        message.ToArray().Should().Equal("message!"u8.ToArray());
    }

    [Fact]
    public void FailedReadDoesNotReleaseASuccessorLockOrRestoreItsMessage()
    {
        using var probe = new QueueProbe(options);
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var destination = new TestMemory();
        var memory = destination.Memory;
        var successorTimestamp = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(1).Ticks;
        destination.OnAccess = () => probe.SetReadLock(successorTimestamp);
        destination.FailReads = true;
        publisher.TryEnqueue("message!"u8).Should().BeTrue();

        Assert.Throws<InvalidOperationException>(() => subscriber.TryDequeue(memory, default, out _));

        probe.ReadLockTimestamp.Should().Be(successorTimestamp);
        probe.HeadState.Should().Be(MessageHeader.LockedToBeConsumedState);
    }

    [Fact]
    public async Task ConcurrentVariableLengthMessagesKeepTheirLengthAndPayloadAsync()
    {
        const int count = 1000;
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var reader = Task.Run(() =>
        {
            var buffer = new byte[200];
            for (var id = 0; id < count; id++)
            {
                var message = subscriber.Dequeue(buffer, cancellation.Token);
                message.Length.Should().Be(8 + (id % 193));
                BitConverter.ToInt32(message.Span).Should().Be(id);
                message.Span[4..].ToArray().Should().OnlyContain(value => value == (byte)id);
            }
        });

        var payload = new byte[200];
        for (var id = 0; id < count; id++)
        {
            var message = payload.AsSpan(0, 8 + (id % 193));
            message.Fill((byte)id);
            BitConverter.TryWriteBytes(message, id).Should().BeTrue();
            while (!publisher.TryEnqueue(message))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Thread.Yield();
            }
        }

        await reader.WaitAsync(TimeSpan.FromSeconds(15));
    }

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
        internal unsafe long ReadLockTimestamp => Interlocked.Read(ref Header->ReadLockTimestamp);

        internal unsafe bool ReadsAreLocked => Interlocked.Read(ref Header->ReadLockTimestamp) != 0;

        internal unsafe int HeadState => ((MessageHeader*)Buffer.GetPointer(Header->ReadOffset))->State;

        internal unsafe void SetReadLock(long timestamp) =>
            Interlocked.Exchange(ref Header->ReadLockTimestamp, timestamp);

        internal unsafe void LockReads() => Interlocked.Exchange(ref Header->ReadLockTimestamp, DateTime.UtcNow.Ticks);

        internal unsafe void AbandonReadLock() =>
            Interlocked.Exchange(ref Header->ReadLockTimestamp, DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(11).Ticks);

        internal unsafe void UnlockReads() => Interlocked.Exchange(ref Header->ReadLockTimestamp, 0);

        internal unsafe void ReserveUnfinishedMessage() => Interlocked.Exchange(ref Header->WriteOffset, 16);
    }

    private sealed class TestMemory : MemoryManager<byte>
    {
        private readonly byte[] bytes = new byte[8];

        internal bool FailReads { get; set; }
        internal bool SupportsPinning { get; set; } = true;
        internal Action? OnAccess { get; set; }

        public override Span<byte> GetSpan()
        {
            CheckAccess();
            return bytes;
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            CheckAccess();
            if (!SupportsPinning)
                throw new NotSupportedException("This memory supports spans but cannot be pinned.");

            return bytes.AsMemory(elementIndex).Pin();
        }

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }

        private void CheckAccess()
        {
            OnAccess?.Invoke();
            if (FailReads)
                throw new InvalidOperationException("Destination memory is unavailable.");
        }
    }
}