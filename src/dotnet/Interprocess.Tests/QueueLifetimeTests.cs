using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using Cloudtoid.Interprocess.Semaphore.Linux;
using Cloudtoid.Interprocess.Semaphore.MacOS;
using Cloudtoid.Interprocess.Semaphore.Posix;
using SysSemaphore = System.Threading.Semaphore;

namespace Cloudtoid.Interprocess.Tests;

public sealed class QueueLifetimeTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    private readonly QueueOptions options = new(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 1024);
    private readonly QueueFactory factory = new();

    [Fact]
    public async Task MismatchedCapacityDoesNotChangeALiveQueueAsync()
    {
        using var child = await StartParticipantAsync("publisher");
        try
        {
            (await CommandAsync(child, "send")).Should().Be("sent");
            var original = OperatingSystem.IsWindows() ? null : await File.ReadAllBytesAsync(BackingFile());
            foreach (var capacity in new long[] { 512, 2048 })
            {
                var mismatched = new QueueOptions(options.QueueName, options.Path, capacity);
                foreach (var publishing in new[] { false, true })
                {
                    Action join = () =>
                    {
                        using var participant = publishing
                            ? (IDisposable)factory.CreatePublisher(mismatched)
                            : factory.CreateSubscriber(mismatched);
                    };
                    join.Should().Throw<ArgumentException>();
                    if (original is not null)
                        (await File.ReadAllBytesAsync(BackingFile())).Should().Equal(original);
                }
            }

            using (var subscriber = factory.CreateSubscriber(options))
            {
                ((Subscriber)subscriber).WaitForNotification(0).Should().BeTrue(
                    "failed joins must preserve existing notifications");
                subscriber.TryDequeue(out var first).Should().BeTrue();
                first.ToArray().Should().Equal("*"u8.ToArray());

                (await CommandAsync(child, "send")).Should().Be("sent");
                ((Subscriber)subscriber).WaitForNotification(1000).Should().BeTrue();
                subscriber.TryDequeue(out var second).Should().BeTrue();
                second.ToArray().Should().Equal("*"u8.ToArray());
                await StopAsync(child);
            }

            AssertResourcesRemoved();
        }
        finally
        {
            KillIfRunning(child);
        }
    }

    [Fact]
    public async Task CapacityCanChangeAfterTheLastParticipantLeavesAsync()
    {
        foreach (var killed in new[] { false, true })
        {
            using var child = await StartParticipantAsync("publisher");
            try
            {
                (await CommandAsync(child, "send")).Should().Be("sent");
                if (killed)
                {
                    child.Kill();
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    if (OperatingSystem.IsWindows())
                        AssertResourcesRemoved(afterForcedExit: true);
                    else
                        File.Exists(BackingFile()).Should().BeTrue();
                }
                else
                {
                    await StopAsync(child);
                    AssertResourcesRemoved();
                }

                var larger = new QueueOptions(options.QueueName, options.Path, 2048);
                using (var subscriber = factory.CreateSubscriber(larger))
                using (var publisher = factory.CreatePublisher(larger))
                {
                    subscriber.TryDequeue(out _).Should().BeFalse();
                    var payload = Enumerable.Range(0, 1800).Select(value => (byte)value).ToArray();
                    publisher.TryEnqueue(payload).Should().BeTrue();
                    subscriber.TryDequeue(out var message).Should().BeTrue();
                    message.ToArray().Should().Equal(payload);
                }

                AssertResourcesRemoved();
            }
            finally
            {
                KillIfRunning(child);
            }
        }
    }

    [Fact]
    public void LastParticipantRemovesMemoryAndSemaphore()
    {
        var publisher = factory.CreatePublisher(options);
        var subscriber = factory.CreateSubscriber(options);
        try
        {
            publisher.TryEnqueue("*"u8).Should().BeTrue();
            publisher.Dispose();
            publisher.Dispose();
            AssertResourcesAlive();
            subscriber.TryDequeue(out _).Should().BeTrue();
        }
        finally
        {
            publisher.Dispose();
            subscriber.Dispose();
            subscriber.Dispose();
        }

        AssertResourcesRemoved();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResourcesSurviveUntilAllPublishersAndSubscribersLeave(bool publishersLeaveFirst)
    {
        using (var publisher1 = factory.CreatePublisher(options))
        using (var publisher2 = factory.CreatePublisher(options))
        using (var subscriber1 = factory.CreateSubscriber(options))
        using (var subscriber2 = factory.CreateSubscriber(options))
        {
            publisher1.TryEnqueue("*"u8).Should().BeTrue();
            publisher1.Dispose();
            subscriber1.Dispose();
            AssertResourcesAlive();

            if (publishersLeaveFirst)
            {
                publisher2.Dispose();
                AssertResourcesAlive();
                subscriber2.TryDequeue(out _).Should().BeTrue();
            }
            else
            {
                subscriber2.Dispose();
                AssertResourcesAlive();
                publisher2.TryEnqueue("*"u8).Should().BeTrue();
            }
        }

        AssertResourcesRemoved();
        using var freshSubscriber = factory.CreateSubscriber(options);
        freshSubscriber.TryDequeue(out _).Should().BeFalse();
    }

    [Fact]
    public async Task PublisherExitKeepsSubscriberAliveAsync() =>
        await CheckParticipantExitAsync("publisher");

    [Fact]
    public async Task SubscriberExitKeepsPublisherAliveAsync() =>
        await CheckParticipantExitAsync("subscriber");

    [Fact]
    public async Task LastProcessExitRemovesResourcesAsync()
    {
        using var child = await StartParticipantAsync("publisher");
        try
        {
            (await CommandAsync(child, "send")).Should().Be("sent");
            await child.StandardInput.WriteLineAsync("process-exit");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            child.ExitCode.Should().Be(0);
            AssertResourcesRemoved();
        }
        finally
        {
            KillIfRunning(child);
        }
    }

    [Fact]
    public async Task NextParticipantRecoversAfterLastProcessIsKilledAsync()
    {
        using var child = await StartParticipantAsync("publisher");
        try
        {
            (await CommandAsync(child, "send")).Should().Be("sent");
            child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (OperatingSystem.IsWindows())
                AssertResourcesRemoved(afterForcedExit: true); // Windows also reclaims handles on forced process exit.
            else
                File.Exists(BackingFile()).Should().BeTrue();

            using (var subscriber = factory.CreateSubscriber(options))
            {
                subscriber.TryDequeue(out _).Should().BeFalse();
                ((Subscriber)subscriber).WaitForNotification(0).Should().BeFalse(
                    "the abandoned semaphore count must be reset too");
                using var publisher = factory.CreatePublisher(options);
                publisher.TryEnqueue("*"u8).Should().BeTrue();
                ((Subscriber)subscriber).WaitForNotification(1000).Should().BeTrue();
                subscriber.TryDequeue(out _).Should().BeTrue();
            }

            AssertResourcesRemoved();
        }
        finally
        {
            KillIfRunning(child);
        }
    }

    [Fact]
    public async Task KilledPublisherDoesNotResetSurvivingSubscriberAsync()
    {
        using var child = await StartParticipantAsync("publisher");
        try
        {
            using (var subscriber = factory.CreateSubscriber(options))
            {
                (await CommandAsync(child, "send")).Should().Be("sent");
                child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

                using var replacement = factory.CreatePublisher(options);
                ((Subscriber)subscriber).WaitForNotification(1000).Should().BeTrue();
                subscriber.TryDequeue(out _).Should().BeTrue("a live subscriber preserves unread messages");
                replacement.TryEnqueue("*"u8).Should().BeTrue();
                ((Subscriber)subscriber).WaitForNotification(1000).Should().BeTrue();
                subscriber.TryDequeue(out _).Should().BeTrue();
            }

            AssertResourcesRemoved(afterForcedExit: true);
        }
        finally
        {
            KillIfRunning(child);
        }
    }

    [Fact]
    public async Task JoiningWhileLastParticipantExitsKeepsResourcesTogetherAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            using var child = await StartParticipantAsync("publisher");
            try
            {
                await child.StandardInput.WriteLineAsync("exit");
                using (var subscriber = factory.CreateSubscriber(options))
                {
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    child.ExitCode.Should().Be(0);
                    using var publisher = factory.CreatePublisher(options);
                    publisher.TryEnqueue("*"u8).Should().BeTrue();
                    ((Subscriber)subscriber).WaitForNotification(1000).Should().BeTrue();
                    subscriber.TryDequeue(out _).Should().BeTrue();
                }

                AssertResourcesRemoved();
            }
            finally
            {
                KillIfRunning(child);
            }
        }
    }

    private async Task CheckParticipantExitAsync(string role)
    {
        using var child = await StartParticipantAsync(role);
        try
        {
            using (var survivor = role == "publisher"
                ? (IDisposable)factory.CreateSubscriber(options)
                : factory.CreatePublisher(options))
            using (var signal = InterprocessSemaphore.CreateWaiter(options.QueueName))
            {
                await StopAsync(child);
                AssertResourcesAlive();
                using var replacement = await StartParticipantAsync("publisher");
                try
                {
                    (await CommandAsync(replacement, "send")).Should().Be("sent");
                    signal.Wait(1000).Should().BeTrue("new participants must use the surviving semaphore");
                    using var subscriber = factory.CreateSubscriber(options);
                    subscriber.TryDequeue(out _).Should().BeTrue();
                    await StopAsync(replacement);
                }
                finally
                {
                    KillIfRunning(replacement);
                }
            }

            AssertResourcesRemoved();
        }
        finally
        {
            KillIfRunning(child);
        }
    }

    private async Task<Process> StartParticipantAsync(string role)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(QueueLifetimeTests).Assembly.Location);
        start.ArgumentList.Add(options.Path);
        start.ArgumentList.Add(options.QueueName);
        start.ArgumentList.Add(role);
        var process = Process.Start(start)!;
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            ready.Should().Be("ready");
            return process;
        }
        catch
        {
            KillIfRunning(process);
            process.Dispose();
            throw;
        }
    }

    private static async Task<string?> CommandAsync(Process process, string command)
    {
        await process.StandardInput.WriteLineAsync(command);
        return await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task StopAsync(Process process)
    {
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        process.ExitCode.Should().Be(0, await process.StandardError.ReadToEndAsync());
    }

    private static void KillIfRunning(Process process)
    {
        if (!process.HasExited)
            process.Kill();
    }

    private string BackingFile() =>
        Path.Combine(options.Path, ".cloudtoid/interprocess/v3/mmf", options.QueueName + ".qu");

    private void AssertResourcesAlive()
    {
        if (OperatingSystem.IsWindows())
        {
            // Open existing objects only, and close the probes before the next departure.
            using var mapping = MemoryMappedFile.OpenExisting("CT3_IP_" + options.QueueName);
            using var semaphore = SysSemaphore.OpenExisting(@"Global\CT3.IP." + options.QueueName);
        }
        else
        {
            File.Exists(BackingFile()).Should().BeTrue();
        }
    }

    private void AssertResourcesRemoved(bool afterForcedExit = false)
    {
        if (OperatingSystem.IsWindows())
        {
            Action openMapping = () =>
            {
                if (OperatingSystem.IsWindows())
                {
                    using var mapping = MemoryMappedFile.OpenExisting("CT3_IP_" + options.QueueName);
                }
            };
            Action openSemaphore = () =>
            {
                if (OperatingSystem.IsWindows())
                {
                    using var semaphore = SysSemaphore.OpenExisting(@"Global\CT3.IP." + options.QueueName);
                }
            };
            // Forced-exit cleanup can outlive the last participant's explicit disposal.
            // Keep ordinary disposal assertions immediate, but allow Windows to finish reclaiming a killed process's mapping.
            if (afterForcedExit)
            {
                SpinWait.SpinUntil(
                    () => Record.Exception(openMapping) is FileNotFoundException,
                    TimeSpan.FromSeconds(5));
            }

            openMapping.Should().Throw<FileNotFoundException>();
            openSemaphore.Should().Throw<WaitHandleCannotBeOpenedException>();
            return;
        }

        File.Exists(BackingFile()).Should().BeFalse();
        Action unlink = () =>
        {
            if (OperatingSystem.IsMacOS())
                SemaphoreMacOS.Unlink(options.QueueName);
            else
                SemaphoreLinux.Unlink(options.QueueName);
        };
        unlink.Should().Throw<PosixSemaphoreNotExistsException>();
    }
}