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
            subscriber.TryDequeue(destination, out var message).Should().BeTrue();
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
            Assert.Throws<InvalidOperationException>(() => subscriber.TryDequeue(memory, out _));
            probe.ReadsAreLocked.Should().BeFalse();
        }

        subscriber.TryDequeue(out var first).Should().BeTrue();
        first.ToArray().Should().Equal("original"u8.ToArray());
        subscriber.TryDequeue(out var second).Should().BeTrue();
        second.ToArray().Should().Equal("next-msg"u8.ToArray());
    }

    [Fact]
    public void DestinationDoesNotNeedToSupportPinning()
    {
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var destination = new TestMemory { SupportsPinning = false };
        publisher.TryEnqueue("message!"u8).Should().BeTrue();

        subscriber.TryDequeue(destination.Memory, out var message).Should().BeTrue();
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

        Assert.Throws<InvalidOperationException>(() => subscriber.TryDequeue(memory, out _));

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
            var attempt = Task.Run(() => subscriber.TryDequeue(out _));
            (await attempt.WaitAsync(TimeSpan.FromSeconds(1))).Should().BeFalse();
        }
        finally
        {
            probe.UnlockReads();
        }

        subscriber.TryDequeue(out _).Should().BeTrue();
    }

    [Fact]
    public void ExpiredSubscriberLockCanBeRecovered()
    {
        using var probe = new QueueProbe(options);
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        publisher.TryEnqueue("message!"u8).Should().BeTrue();
        probe.AbandonReadLock();
        subscriber.TryDequeue(out var message).Should().BeTrue();
        message.ToArray().Should().Equal("message!"u8.ToArray());
        probe.ReadsAreLocked.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryDequeueReturnsPromptlyForUnfinishedMessageAsync(bool reuseBuffer)
    {
        using var probe = new QueueProbe(options);
        using var subscriber = factory.CreateSubscriber(options);
        probe.ReserveUnfinishedMessage();
        var buffer = new byte[8];
        var attempts = Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                var received = reuseBuffer
                    ? subscriber.TryDequeue(buffer, out var message)
                    : subscriber.TryDequeue(out message);
                received.Should().BeFalse();
                message.IsEmpty.Should().BeTrue();
            }
        });

        await attempts.WaitAsync(TimeSpan.FromSeconds(1));
        probe.CompleteMessage();
        var success = reuseBuffer
            ? subscriber.TryDequeue(buffer, out var result)
            : subscriber.TryDequeue(out result);
        success.Should().BeTrue();
        result.ToArray().Should().Equal("message!"u8.ToArray());
        probe.ReadsAreLocked.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockingDequeueWaitsForPublisherToCompleteAsync(bool reuseBuffer)
    {
        using var probe = new QueueProbe(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        probe.ReserveUnfinishedMessage();
        subscriber.TryDequeue(out _).Should().BeFalse();
        var read = Task.Run(() => reuseBuffer
            ? subscriber.Dequeue(new byte[8], cancellation.Token)
            : subscriber.Dequeue(cancellation.Token));
        await Task.Delay(20);
        read.IsCompleted.Should().BeFalse();
        probe.CompleteMessage();

        (await read.WaitAsync(TimeSpan.FromSeconds(1))).ToArray().Should().Equal("message!"u8.ToArray());
        probe.ReadsAreLocked.Should().BeFalse();
    }

    [Fact]
    public void DisposingSubscriberReleasesPendingReservation()
    {
        using var probe = new QueueProbe(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var successor = factory.CreateSubscriber(options);
        probe.ReserveUnfinishedMessage();
        subscriber.TryDequeue(out _).Should().BeFalse();
        probe.ReadsAreLocked.Should().BeTrue();

        subscriber.Dispose();

        probe.ReadsAreLocked.Should().BeFalse();
        probe.CompleteMessage();
        successor.TryDequeue(out var message).Should().BeTrue();
        message.ToArray().Should().Equal("message!"u8.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryDequeueAfterDisposalIsRejected(bool reuseBuffer)
    {
        using var subscriber = factory.CreateSubscriber(options);
        subscriber.Dispose();

        Assert.Throws<OperationCanceledException>(() =>
        {
            if (reuseBuffer)
                subscriber.TryDequeue(new byte[8], out _);
            else
                subscriber.TryDequeue(out _);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingReservationDoesNotReleaseASuccessorLock(bool dispose)
    {
        using var probe = new QueueProbe(options);
        using var subscriber = factory.CreateSubscriber(options);
        probe.ReserveUnfinishedMessage();
        subscriber.TryDequeue(out _).Should().BeFalse();
        var successorTimestamp = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(1).Ticks;
        probe.SetReadLock(successorTimestamp);

        if (dispose)
            subscriber.Dispose();
        else
            subscriber.TryDequeue(out _).Should().BeFalse();

        probe.ReadLockTimestamp.Should().Be(successorTimestamp);
    }

    [Fact]
    public void PendingReservationCanBeTakenOverAfterItsLockExpires()
    {
        using var probe = new QueueProbe(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var successor = factory.CreateSubscriber(options);
        probe.ReserveUnfinishedMessage();
        subscriber.TryDequeue(out _).Should().BeFalse();
        probe.AbandonReadLock();
        successor.TryDequeue(out _).Should().BeFalse();
        subscriber.TryDequeue(out _).Should().BeFalse();
        probe.CompleteMessage();

        successor.TryDequeue(out var message).Should().BeTrue();
        message.ToArray().Should().Equal("message!"u8.ToArray());
        subscriber.TryDequeue(out _).Should().BeFalse();
        probe.ReadsAreLocked.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentCallsOnOneSubscriberConsumePendingMessageOnceAsync()
    {
        using var probe = new QueueProbe(options);
        using var subscriber = factory.CreateSubscriber(options);
        for (var lap = 0; lap < 40; lap++)
        {
            probe.ReserveUnfinishedMessage();
            subscriber.TryDequeue(out _).Should().BeFalse();
            var unfinishedAttempts = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 32; i++)
                {
                    subscriber.TryDequeue(out var message).Should().BeFalse();
                    message.IsEmpty.Should().BeTrue();
                }
            }));
            await Task.WhenAll(unfinishedAttempts).WaitAsync(TimeSpan.FromSeconds(1));
            probe.CompleteMessage();
            var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                var received = subscriber.TryDequeue(out var message);
                if (received)
                    message.ToArray().Should().Equal("message!"u8.ToArray());

                return received;
            }));

            var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(1));
            results.Count(received => received).Should().Be(1);
            probe.ReadsAreLocked.Should().BeFalse();
        }
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationInterruptsUnfinishedMessageAsync(bool reuseBuffer)
    {
        using var probe = new QueueProbe(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var cancellation = new CancellationTokenSource();
        probe.ReserveUnfinishedMessage();
        var read = Task.Run(() => reuseBuffer
            ? subscriber.Dequeue(new byte[8], cancellation.Token)
            : subscriber.Dequeue(cancellation.Token));

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
        const int count = 20000;
        var options = new QueueOptions(this.options.QueueName, this.options.Path, 120);
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
                var buffer = new byte[64];
                for (var i = 0; i < count / subscriberCount; i++)
                {
                    var message = subscriber.Dequeue(buffer, cancellation.Token);
                    var id = BitConverter.ToInt32(message.Span);
                    id.Should().BeInRange(0, count - 1);
                    message.Length.Should().Be(8 + (id % 57));
                    message.Span[4..].ToArray().Should().OnlyContain(value => value == (byte)id);
                    Interlocked.Increment(ref received[id]);
                }
            });
        }

        var writers = new Task[4];
        for (var writer = 0; writer < writers.Length; writer++)
        {
            var firstId = writer;
            writers[writer] = Task.Run(() =>
            {
                using var publisher = factory.CreatePublisher(options);
                var buffer = new byte[64];
                for (var id = firstId; id < count; id += writers.Length)
                {
                    var message = buffer.AsSpan(0, 8 + (id % 57));
                    message.Fill((byte)id);
                    BitConverter.TryWriteBytes(message, id).Should().BeTrue();
                    while (!publisher.TryEnqueue(message))
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

        internal unsafe void ReserveUnfinishedMessage() =>
            Interlocked.Exchange(ref Header->WriteOffset, checked(Header->WriteOffset + 16));

        internal unsafe void CompleteMessage()
        {
            var readOffset = Header->ReadOffset;
            var messageHeader = (MessageHeader*)Buffer.GetPointer(readOffset);
            Buffer.Write("message!"u8, GetMessageBodyOffset(readOffset));
            messageHeader->BodyLength = 8;
            Volatile.Write(ref messageHeader->State, MessageHeader.ReadyToBeConsumedState);
        }
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