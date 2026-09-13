using System.Buffers;
using System.Diagnostics;

namespace Cloudtoid.Interprocess.Tests;

public sealed class SubscriberDisposalTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    [Theory]
    [InlineData("late-dispose")]
    [InlineData("late-cancel")]
    [InlineData("copy-try")]
    [InlineData("copy-dequeue")]
    [InlineData("copy-failure")]
    [InlineData("race-try")]
    [InlineData("race-dequeue")]
    public async Task SubscriberDisposalIsSafeInChildProcessAsync(string scenario)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(SubscriberDisposalTests).Assembly.Location);
        start.ArgumentList.Add(fixture.Path);
        start.ArgumentList.Add(Guid.NewGuid().ToStringInvariant("N")[..16]);
        start.ArgumentList.Add("subscriber-disposal");
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
        ThreadPool.SetMinThreads(8, 8).Should().BeTrue();
        switch (scenario)
        {
            case "late-dispose":
                await CheckLateAdmissionAsync(options, true);
                break;
            case "late-cancel":
                await CheckLateAdmissionAsync(options, false);
                break;
            case "copy-try":
                await CheckPausedCopyAsync(options, false, false);
                break;
            case "copy-dequeue":
                await CheckPausedCopyAsync(options, true, false);
                break;
            case "copy-failure":
                await CheckPausedCopyAsync(options, true, true);
                break;
            case "race-try":
                await CheckRacesAsync(options, false);
                break;
            case "race-dequeue":
                await CheckRacesAsync(options, true);
                break;
            default:
                throw new ArgumentException("Unknown subscriber disposal scenario.", nameof(scenario));
        }

        await Console.Out.WriteLineAsync("passed");
    }

    private static async Task CheckLateAdmissionAsync(QueueOptions options, bool dispose)
    {
        using var subscriber = new Subscriber(options, NullLoggerFactory.Instance);
        using var cancellation = new CancellationTokenSource();
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admission = Task.Run(async () =>
        {
            // Model a caller paused after its initial check, immediately before admission.
            cancellation.Token.ThrowIfCancellationRequested();
            paused.SetResult();
            await resume.Task;
            return Assert.Throws<OperationCanceledException>(() => subscriber.EnterRead(cancellation.Token));
        });
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (dispose)
            subscriber.Dispose();
        else
            await cancellation.CancelAsync();

        resume.SetResult();
        var error = await admission.WaitAsync(TimeSpan.FromSeconds(5));
        if (!dispose)
        {
            error.CancellationToken.Should().Be(cancellation.Token);
            using var publisher = new QueueFactory().CreatePublisher(options);
            publisher.TryEnqueue("message!"u8).Should().BeTrue();
            subscriber.TryDequeue(out var message).Should().BeTrue();
            message.ToArray().Should().Equal("message!"u8.ToArray());
        }

        // Failed admission must balance the count, and repeated disposal must remain harmless.
        await Task.Run(subscriber.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        subscriber.Dispose();
        Assert.Throws<OperationCanceledException>(() => subscriber.TryDequeue(out _));
        Assert.Throws<OperationCanceledException>(() => subscriber.TryDequeue(new byte[8], out _));
        Assert.Throws<OperationCanceledException>(() => subscriber.Dequeue(default));
        Assert.Throws<OperationCanceledException>(() => subscriber.Dequeue(new byte[8], default));
    }

    private static async Task CheckPausedCopyAsync(QueueOptions options, bool blocking, bool failRead)
    {
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var memory = new PausedMemory(entered, resume, failRead);
        var buffer = memory.Memory;
        memory.PauseReads = true;
        var factory = new QueueFactory();
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var successor = factory.CreateSubscriber(options);
        publisher.TryEnqueue("message!"u8).Should().BeTrue();
        var read = Task.Run(() =>
        {
            if (blocking)
                return subscriber.Dequeue(buffer, default);

            subscriber.TryDequeue(buffer, out var message).Should().BeTrue();
            return message;
        });
        Task disposal;
        try
        {
            entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            disposal = Task.Run(subscriber.Dispose);
            var closed = SpinWait.SpinUntil(
                () =>
                {
                    try
                    {
                        subscriber.TryDequeue(out _).Should().BeFalse();
                        return false;
                    }
                    catch (OperationCanceledException)
                    {
                        return true;
                    }
                },
                TimeSpan.FromSeconds(5));
            closed.Should().BeTrue();
            disposal.IsCompleted.Should().BeFalse("the admitted copy is still using the mapped view");
        }
        finally
        {
            resume.Set();
        }

        if (failRead)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await read.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        else
        {
            (await read.WaitAsync(TimeSpan.FromSeconds(5))).ToArray().Should().Equal("message!"u8.ToArray());
        }

        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if (failRead)
            successor.Dequeue(cancellation.Token).ToArray().Should().Equal("message!"u8.ToArray());

        publisher.TryEnqueue("next-msg"u8).Should().BeTrue();
        successor.Dequeue(cancellation.Token).ToArray().Should().Equal("next-msg"u8.ToArray());
    }

    private static async Task CheckRacesAsync(QueueOptions options, bool blocking)
    {
        var factory = new QueueFactory();
        for (var iteration = 0; iteration < 32; iteration++)
        {
            using var publisher = factory.CreatePublisher(options);
            using var subscriber = factory.CreateSubscriber(options);
            using var started = new CountdownEvent(4);
            for (var i = 0; i < 16; i++)
                publisher.TryEnqueue("message!"u8).Should().BeTrue();

            var readers = Enumerable.Range(0, 4)
                .Select(index => Task.Run(() =>
                {
                    var buffer = new byte[8];
                    started.Signal();
                    while (true)
                    {
                        try
                        {
                            if (blocking)
                            {
                                var message = index % 2 == 0
                                    ? subscriber.Dequeue(default)
                                    : subscriber.Dequeue(buffer, default);
                                message.ToArray().Should().Equal("message!"u8.ToArray());
                            }
                            else
                            {
                                var received = index % 2 == 0
                                    ? subscriber.TryDequeue(out var message)
                                    : subscriber.TryDequeue(buffer, out message);
                                if (received)
                                    message.ToArray().Should().Equal("message!"u8.ToArray());
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                }))
                .ToArray();
            started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            if (iteration % 2 == 0)
                await Task.Delay(1);

            var disposal = Task.Run(subscriber.Dispose);
            await Task.WhenAll(readers.Append(disposal)).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class PausedMemory(
        ManualResetEventSlim entered,
        ManualResetEventSlim resume,
        bool failRead) : MemoryManager<byte>
    {
        private readonly byte[] bytes = new byte[8];

        internal bool PauseReads { get; set; }

        public override Span<byte> GetSpan()
        {
            if (PauseReads)
            {
                entered.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The admitted reader was not resumed.");

                if (failRead)
                    throw new InvalidOperationException("Simulated destination failure.");
            }

            return bytes;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}