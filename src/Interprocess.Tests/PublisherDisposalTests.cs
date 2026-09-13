using System.Diagnostics;

namespace Cloudtoid.Interprocess.Tests;

public sealed class PublisherDisposalTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    [Theory]
    [InlineData("after-dispose")]
    [InlineData("drain-one")]
    [InlineData("drain-many")]
    [InlineData("release-failure")]
    [InlineData("race-empty")]
    [InlineData("race-large")]
    public async Task PublisherDisposalIsSafeInChildProcessAsync(string scenario)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(PublisherDisposalTests).Assembly.Location);
        start.ArgumentList.Add(fixture.Path);
        start.ArgumentList.Add(Guid.NewGuid().ToStringInvariant("N")[..16]);
        start.ArgumentList.Add("publisher-disposal");
        start.ArgumentList.Add(scenario);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            process.ExitCode.Should().Be(0, await errors);
            (await output).Trim().Should().EndWith("passed");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    internal static async Task RunChildAsync(QueueOptions options, string scenario)
    {
        // Blocking test signals must not starve the disposal task on small CI machines.
        ThreadPool.SetMinThreads(8, 8).Should().BeTrue();
        switch (scenario)
        {
            case "after-dispose":
                CheckCallsAfterDisposal(options);
                break;
            case "drain-one":
                await CheckDrainingAsync(options, 1, false);
                break;
            case "drain-many":
                await CheckDrainingAsync(options, 4, false);
                break;
            case "release-failure":
                await CheckDrainingAsync(options, 4, true);
                break;
            case "race-empty":
                await CheckRacesAsync(options, 0);
                break;
            case "race-large":
                await CheckRacesAsync(options, 65536);
                break;
            default:
                throw new ArgumentException("Unknown publisher disposal scenario.", nameof(scenario));
        }

        await Console.Out.WriteLineAsync("passed");
    }

    private static void CheckCallsAfterDisposal(QueueOptions options)
    {
        var factory = new QueueFactory();
        using var publisher = factory.CreatePublisher(options);
        publisher.Dispose();
        publisher.Dispose();
        foreach (var length in new[] { 0, 1, 2048 })
        {
            var payload = new byte[length];
            Assert.Throws<ObjectDisposedException>(() => publisher.TryEnqueue(payload));
        }
    }

    private static async Task CheckDrainingAsync(QueueOptions options, int writers, bool failRelease)
    {
        // Only one writer posts; the others complete while its notification is pending.
        using var entered = new CountdownEvent(1);
        using var resume = new ManualResetEventSlim();
        using var signal = new GatedSignal(entered, resume, failRelease);
        using var publisher = new Publisher(options, NullLoggerFactory.Instance, signal);
        using var subscriber = new QueueFactory().CreateSubscriber(options);
        var sends = Enumerable.Range(0, writers)
            .Select(_ => Task.Run(() => publisher.TryEnqueue("message!"u8)))
            .ToArray();
        Task disposal;
        try
        {
            entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            var coalesced = SpinWait.SpinUntil(
                () => sends.Count(send => send.IsCompleted) == writers - 1,
                TimeSpan.FromSeconds(5));
            coalesced.Should().BeTrue();
            disposal = Task.Run(publisher.Dispose);
            var oversized = new byte[2048];
            var closed = SpinWait.SpinUntil(
                () =>
                {
                    try
                    {
                        publisher.TryEnqueue(oversized).Should().BeFalse();
                        return false;
                    }
                    catch (ObjectDisposedException)
                    {
                        return true;
                    }
                },
                TimeSpan.FromSeconds(5));
            closed.Should().BeTrue("disposal must close admission before draining");

            disposal.IsCompleted.Should().BeFalse("admitted enqueues are still using the signal");
            signal.IsDisposed.Should().BeFalse();
        }
        finally
        {
            resume.Set();
        }

        var failures = 0;
        foreach (var send in sends)
        {
            try
            {
                (await send.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
            }
            catch (InvalidOperationException) when (failRelease)
            {
                failures++;
            }
        }

        failures.Should().Be(failRelease ? 1 : 0, "only the writer posting a notification can fail its release");
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        signal.IsDisposed.Should().BeTrue();
        // Even a failed notification happens after the message is committed.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var i = 0; i < writers; i++)
            subscriber.Dequeue(cancellation.Token).ToArray().Should().Equal("message!"u8.ToArray());

        Assert.Throws<ObjectDisposedException>(() => publisher.TryEnqueue("late"u8));
    }

    private static async Task CheckRacesAsync(QueueOptions originalOptions, int messageLength)
    {
        var options = new QueueOptions(
            originalOptions.QueueName,
            originalOptions.Path,
            messageLength == 0 ? 1024 : 262144);
        var factory = new QueueFactory();
        var payload = new byte[messageLength];
        payload.AsSpan().Fill(42);
        for (var iteration = 0; iteration < 32; iteration++)
        {
            using var publisher = factory.CreatePublisher(options);
            using var subscriber = factory.CreateSubscriber(options);
            using var started = new CountdownEvent(4);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var accepted = 0;
            var writers = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() =>
                {
                    started.Signal();
                    while (true)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        try
                        {
                            if (publisher.TryEnqueue(payload))
                                Interlocked.Increment(ref accepted);
                        }
                        catch (ObjectDisposedException)
                        {
                            return;
                        }
                    }
                }))
                .ToArray();

            started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            if (iteration % 2 == 0)
                await Task.Delay(1);

            publisher.Dispose();
            await Task.WhenAll(writers).WaitAsync(TimeSpan.FromSeconds(5));
            var buffer = new byte[messageLength];
            for (var i = 0; i < accepted; i++)
            {
                var message = subscriber.Dequeue(buffer, cancellation.Token);
                message.Span.SequenceEqual(payload).Should().BeTrue();
            }
        }
    }

    private sealed class GatedSignal(
        CountdownEvent entered,
        ManualResetEventSlim resume,
        bool failRelease) : IInterprocessSemaphoreReleaser
    {
        private int disposed;

        internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public void Release()
        {
            entered.Signal();
            if (!resume.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The admitted publisher was not resumed.");

            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (failRelease)
                throw new InvalidOperationException("Simulated notification failure.");
        }

        public void Dispose() => Interlocked.Exchange(ref disposed, 1);
    }
}