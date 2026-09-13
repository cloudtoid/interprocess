namespace Cloudtoid.Interprocess.Tests;

public sealed class RecoveryTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardedMessagesCannotReappearAfterWrapAsync(bool blocking)
    {
        // Exercise physical wrap, offset wrap at twice capacity, and full-buffer discards.
        var cases = from start in new[] { 0, 48, 112 }
                    from records in new[] { 2, 4 }
                    from reuseBuffer in new[] { false, true }
                    select CheckRecoveryAsync(start, records, blocking, reuseBuffer);
        await Task.WhenAll(cases);
    }

    private async Task CheckRecoveryAsync(int start, int records, bool blocking, bool reuseBuffer)
    {
        const int capacity = 64;
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, capacity);
        var factory = new QueueFactory();
        using var probe = new RecoveryProbe(options);
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        var buffer = new byte[8];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        for (var offset = 0; offset < start; offset += 16)
        {
            publisher.TryEnqueue("advance!"u8).Should().BeTrue();
            subscriber.TryDequeue(out _).Should().BeTrue();
        }

        probe.ReadOffset.Should().Be(start);
        probe.ReserveUnfinishedMessage();
        for (var i = 1; i < records; i++)
            publisher.TryEnqueue("old-body"u8).Should().BeTrue();

        subscriber.TryDequeue(out _).Should().BeFalse(); // Capture the discard boundary and start the real timeout.
        if (records < 4)
            publisher.TryEnqueue("survivor"u8).Should().BeTrue(); // Beyond the captured boundary.

        var receive = Task.Run(async () =>
        {
            if (blocking)
                return reuseBuffer ? subscriber.Dequeue(buffer, timeout.Token) : subscriber.Dequeue(timeout.Token);

            return await PollAsync(subscriber, buffer, reuseBuffer, timeout.Token);
        });

        try
        {
            if (records == 4)
            {
                // A full buffer cannot accept the survivor until recovery has released the discarded range.
                while (probe.ReadOffset == start)
                    await Task.Delay(10, timeout.Token);

                publisher.TryEnqueue("survivor"u8).Should().BeTrue();
            }

            (await receive).ToArray().Should().Equal("survivor"u8.ToArray());
        }
        finally
        {
            if (!receive.IsCompleted)
            {
                await timeout.CancelAsync();
                try
                {
                    await receive;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        // Advance to a discarded ready header's physical slot, then reserve it without completing the write.
        var target = (start + 16) % capacity;
        while (probe.WriteOffset % capacity != target)
        {
            publisher.TryEnqueue("advance!"u8).Should().BeTrue();
            subscriber.TryDequeue(out _).Should().BeTrue();
        }

        var reservation = probe.WriteOffset;
        probe.ReserveUnfinishedMessage();
        if (blocking)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            Assert.Throws<OperationCanceledException>(() => reuseBuffer
                ? subscriber.Dequeue(buffer, cancellation.Token)
                : subscriber.Dequeue(cancellation.Token));
        }
        else
        {
            var received = reuseBuffer
                ? subscriber.TryDequeue(buffer, out var message)
                : subscriber.TryDequeue(out message);
            received.Should().BeFalse("a discarded ready header must not deliver its old payload on a later lap");
            message.IsEmpty.Should().BeTrue();
        }

        probe.ReadBytes(start, records * 16).Should().OnlyContain(value => value == 0);
        probe.CompleteMessage(reservation);
        var completed = await PollAsync(subscriber, buffer, reuseBuffer, timeout.Token);
        completed.ToArray().Should().Equal("new-body"u8.ToArray());
    }

    private static async Task<ReadOnlyMemory<byte>> PollAsync(
        ISubscriber subscriber,
        Memory<byte> buffer,
        bool reuseBuffer,
        CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var received = reuseBuffer
                ? subscriber.TryDequeue(buffer, out var message)
                : subscriber.TryDequeue(out message);
            if (received)
                return message;

            await Task.Delay(10, cancellation);
        }
    }

    private sealed class RecoveryProbe(QueueOptions options) : Queue(options, NullLoggerFactory.Instance)
    {
        internal unsafe long ReadOffset => Interlocked.Read(ref Header->ReadOffset);
        internal unsafe long WriteOffset => Interlocked.Read(ref Header->WriteOffset);

        internal byte[] ReadBytes(long offset, long length) => Buffer.Read(offset, length).ToArray();

        internal unsafe void ReserveUnfinishedMessage() =>
            Interlocked.Exchange(ref Header->WriteOffset, SafeIncrementMessageOffset(WriteOffset, 16));

        internal unsafe void CompleteMessage(long offset)
        {
            Buffer.Write("new-body"u8, GetMessageBodyOffset(offset));
            var header = (MessageHeader*)Buffer.GetPointer(offset);
            header->BodyLength = 8;
            Volatile.Write(ref header->State, MessageHeader.ReadyToBeConsumedState);
        }
    }
}