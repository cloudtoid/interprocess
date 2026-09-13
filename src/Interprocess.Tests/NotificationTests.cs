using SysSemaphore = System.Threading.Semaphore;

namespace Cloudtoid.Interprocess.Tests;

public sealed class NotificationTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    private readonly QueueOptions options = new(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 64);

    [Fact]
    public void ImmediateTrafficLeavesOnlyOneNativePermit()
    {
        using var publisher = new Publisher(options, NullLoggerFactory.Instance);
        using var subscriber = new Subscriber(options, NullLoggerFactory.Instance);
        var buffer = new byte[8];
        for (var i = 0; i < 100000; i++)
        {
            publisher.TryEnqueue("payload!"u8).Should().BeTrue();
            subscriber.TryDequeue(buffer, out _).Should().BeTrue();
        }

        subscriber.WaitForNotification(0).Should().BeTrue();
        subscriber.WaitForNotification(0).Should().BeFalse("an empty queue must not retain one permit per message");
        publisher.TryEnqueue("next-msg"u8).Should().BeTrue();
        subscriber.WaitForNotification(0).Should().BeTrue("consuming the permit rearms notification");
        subscriber.WaitForNotification(0).Should().BeFalse();
        subscriber.TryDequeue(out var message).Should().BeTrue();
        message.ToArray().Should().Equal("next-msg"u8.ToArray());
    }

    [Fact]
    public void PublicationImmediatelyBeforeWaitIsObserved()
    {
        using var handle = new SysSemaphore(0, int.MaxValue);
        using var signal = new TestSignal(handle);
        using var publisher = new Publisher(options, NullLoggerFactory.Instance, signal);
        using var subscriber = new Subscriber(options, NullLoggerFactory.Instance, signal);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        signal.BeforeWait = () => publisher.TryEnqueue("payload!"u8).Should().BeTrue();

        subscriber.Dequeue(cancellation.Token).ToArray().Should().Equal("payload!"u8.ToArray());
        signal.Releases.Should().Be(1);
        signal.PositiveWaits.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationBeforeAcknowledgementSurvivesCancellation(bool cancel)
    {
        using var handle = new SysSemaphore(0, int.MaxValue);
        using var signal = new TestSignal(handle);
        using var publisher = new Publisher(options, NullLoggerFactory.Instance, signal);
        using var subscriber = new Subscriber(options, NullLoggerFactory.Instance, signal);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        publisher.TryEnqueue("old-body"u8).Should().BeTrue();
        subscriber.TryDequeue(out _).Should().BeTrue();
        signal.AfterWait = () =>
        {
            // The permit is consumed, but the flag is still set: this publication must coalesce.
            publisher.TryEnqueue("new-body"u8).Should().BeTrue();
            if (cancel)
                cancellation.Cancel();
        };

        if (cancel)
        {
            Assert.Throws<OperationCanceledException>(() => subscriber.Dequeue(cancellation.Token));
            signal.AfterWait = null;
            using var successor = new Subscriber(options, NullLoggerFactory.Instance, signal);
            successor.WaitForNotification(0).Should().BeTrue("the cancelled reader must pass on its wakeup");
            successor.TryDequeue(out var message).Should().BeTrue();
            message.ToArray().Should().Equal("new-body"u8.ToArray());
            signal.Releases.Should().Be(2);
        }
        else
        {
            subscriber.Dequeue(cancellation.Token).ToArray().Should().Equal("new-body"u8.ToArray());
            signal.Releases.Should().Be(1, "the post-consumption recheck must observe the coalesced publication");
        }

        signal.PositiveWaits.Should().Be(1);
    }

    [Fact]
    public async Task OneCoalescedBurstWakesEveryBlockedReaderAsync()
    {
        const int readerCount = 4;
        using var handle = new SysSemaphore(0, int.MaxValue);
        using var signal = new TestSignal(handle);
        using var publisher = new Publisher(options, NullLoggerFactory.Instance, signal);
        using var waiting = new CountdownEvent(readerCount);
        using var posting = new ManualResetEventSlim();
        using var resumePost = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitCalls = 0;
        signal.BeforeWait = () =>
        {
            if (Interlocked.Increment(ref waitCalls) <= readerCount)
                waiting.Signal();
        };
        var postCalls = 0;
        signal.BeforeRelease = () =>
        {
            if (Interlocked.Increment(ref postCalls) == 1)
            {
                posting.Set();
                resumePost.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            }
        };
        var readers = new Task<byte[]>[readerCount];
        for (var i = 0; i < readerCount; i++)
        {
            readers[i] = Task.Factory.StartNew(
                () =>
                {
                    using var subscriber = new Subscriber(options, NullLoggerFactory.Instance, signal);
                    return subscriber.Dequeue(cancellation.Token).ToArray();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        Task<bool>? firstPublish = null;
        try
        {
            (await Task.Run(() => waiting.Wait(TimeSpan.FromSeconds(5)))).Should().BeTrue();
            firstPublish = Task.Run(() => publisher.TryEnqueue([0]));
            (await Task.Run(() => posting.Wait(TimeSpan.FromSeconds(5)))).Should().BeTrue();
            for (var i = 1; i < readerCount; i++)
                publisher.TryEnqueue([(byte)i]).Should().BeTrue();

            signal.Releases.Should().Be(1, "all messages are committed before the first native post completes");
            resumePost.Set();
            (await firstPublish).Should().BeTrue();
            var messages = await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(10));
            messages.Select(message => message.Single()).Order().Should().Equal([0, 1, 2, 3]);
            signal.Releases.Should().Be(readerCount, "woken readers pass the permit down the waiting chain");
            signal.PositiveWaits.Should().Be(readerCount, "polling timeouts must not rescue the wakeup chain");
        }
        finally
        {
            resumePost.Set();
            await cancellation.CancelAsync();
            if (firstPublish is not null)
                await firstPublish;

            await Task.WhenAll(readers);
        }
    }

    [Fact]
    public async Task DelayedPostAndTimedOutWaitsDoNotAccumulatePermitsAsync()
    {
        using var handle = new SysSemaphore(0, int.MaxValue);
        using var signal = new TestSignal(handle);
        using var publisher = new Publisher(options, NullLoggerFactory.Instance, signal);
        using var subscriber = new Subscriber(options, NullLoggerFactory.Instance, signal);
        using var posting = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        signal.BeforeRelease = () =>
        {
            posting.Set();
            resume.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        };
        var first = Task.Run(() => publisher.TryEnqueue("first"u8));
        try
        {
            (await Task.Run(() => posting.Wait(TimeSpan.FromSeconds(5)))).Should().BeTrue();
            subscriber.TryDequeue(out _).Should().BeTrue();
            for (var i = 0; i < 1000; i++)
            {
                subscriber.WaitForNotification(0).Should().BeFalse();
                publisher.TryEnqueue("next"u8).Should().BeTrue();
                subscriber.TryDequeue(out _).Should().BeTrue();
            }

            signal.Releases.Should().Be(1);
        }
        finally
        {
            resume.Set();
            (await first).Should().BeTrue();
        }

        subscriber.WaitForNotification(0).Should().BeTrue();
        subscriber.WaitForNotification(0).Should().BeFalse();
    }

    private sealed class TestSignal(SysSemaphore handle) : IInterprocessSemaphoreWaiter
    {
        private int releases;
        private int positiveWaits;

        internal Action? BeforeWait { get; set; }
        internal Action? AfterWait { get; set; }
        internal Action? BeforeRelease { get; set; }
        internal int Releases => Volatile.Read(ref releases);
        internal int PositiveWaits => Volatile.Read(ref positiveWaits);

        public void Release()
        {
            Interlocked.Increment(ref releases);
            BeforeRelease?.Invoke();
            handle.Release();
        }

        public bool Wait(int millisecondsTimeout)
        {
            if (millisecondsTimeout > 0)
                Interlocked.Increment(ref positiveWaits);

            BeforeWait?.Invoke();
            // Disable the normal five-millisecond fallback so it cannot conceal a missed wakeup.
            var consumed = handle.WaitOne(millisecondsTimeout == 0 ? 0 : 5000);
            if (!consumed && millisecondsTimeout > 0)
                throw new TimeoutException("A reader missed its notification.");

            if (consumed)
                AfterWait?.Invoke();

            return consumed;
        }

        // Participants share this test handle. The test owns its lifetime.
        public void Dispose()
        {
        }
    }
}