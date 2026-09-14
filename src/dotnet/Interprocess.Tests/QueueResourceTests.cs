using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using NSubstitute;

namespace Cloudtoid.Interprocess.Tests;

public sealed class QueueResourceTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    public void InvalidQueueNamesAreRejectedBeforeCreatingResources(string name)
    {
        Action create = () => _ = new QueueOptions(name, fixture.Path, 1024);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void QueueNameLimitCountsUtf8Bytes()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return;

        var limit = OperatingSystem.IsMacOS() ? 24 : 245;
        var name = new string('é', (limit / 2) + 1);
        Action create = () => _ = new QueueOptions(name, fixture.Path, 1024);
        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignalDisposalFailureStillReleasesQueueResources(bool publishing)
    {
        var name = Guid.NewGuid().ToStringInvariant("N")[..16];
        var options = new QueueOptions(name, fixture.Path, 1024);
        var signal = NSubstitute.Substitute.For<IInterprocessSemaphoreWaiter>();
        signal.When(value => value.Dispose()).Do(_ => throw new InvalidOperationException("close failed"));
        using var participant = publishing
            ? (IDisposable)new Publisher(options, NullLoggerFactory.Instance, signal)
            : new Subscriber(options, NullLoggerFactory.Instance, signal);
        Action dispose = participant.Dispose;
        dispose.Should().Throw<InvalidOperationException>().WithMessage("close failed");

        // A changed capacity proves the failed close did not keep the old mapping alive.
        var fresh = new QueueOptions(name, fixture.Path, 2048);
        var factory = new QueueFactory();
        using var publisher = factory.CreatePublisher(fresh);
        using var subscriber = factory.CreateSubscriber(fresh);
        publisher.TryEnqueue("fresh"u8).Should().BeTrue();
        subscriber.TryDequeue(out var message).Should().BeTrue();
        message.ToArray().Should().Equal("fresh"u8.ToArray());
    }

    [Fact]
    public async Task ConcurrentFirstParticipantsMustAgreeOnCapacityAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var name = Guid.NewGuid().ToStringInvariant("N")[..16];
            using var start = new ManualResetEventSlim();
            var options = new[]
            {
                new QueueOptions(name, fixture.Path, 512),
                new QueueOptions(name, fixture.Path, 2048)
            };
            var factory = new QueueFactory();
            IPublisher? Join(QueueOptions option)
            {
                start.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                try
                {
                    return factory.CreatePublisher(option);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            var joins = options.Select(option => Task.Run(() => Join(option))).ToArray();
            start.Set();
            var publishers = await Task.WhenAll(joins).WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                publishers.Count(value => value is not null).Should().Be(1);
                var winner = Array.FindIndex(publishers, value => value is not null);
                using var subscriber = factory.CreateSubscriber(options[winner]);
                publishers[winner]!.TryEnqueue("winner"u8).Should().BeTrue();
                subscriber.TryDequeue(out var message).Should().BeTrue();
                message.ToArray().Should().Equal("winner"u8.ToArray());
            }
            finally
            {
                foreach (var publisher in publishers)
                    publisher?.Dispose();
            }
        }
    }

    [Fact(Platforms = Platform.Windows)]
    public async Task KilledInitializerDoesNotLeaveAnUnknownCapacityAsync()
    {
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 2048);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        var arguments = new[] { typeof(Program).Assembly.Location, options.Path, options.QueueName, "queue-init" };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var child = Process.Start(start)!;
        try
        {
            var ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            ready.Should().Be("initializing");
            var factory = new QueueFactory();
            var join = Task.Run(() => factory.CreatePublisher(options));
            child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            using var publisher = await join.WaitAsync(TimeSpan.FromSeconds(10));
            using var subscriber = factory.CreateSubscriber(options);
            var payload = new byte[1800];
            publisher.TryEnqueue(payload).Should().BeTrue();
            subscriber.TryDequeue(out var message).Should().BeTrue();
            message.ToArray().Should().Equal(payload);
        }
        finally
        {
            if (!child.HasExited)
                child.Kill();
        }
    }

    [Fact]
    public void InvalidCapacityFailsBeforeOpeningQueueResources()
    {
        Action unaligned = () => _ = new QueueOptions("invalid", 25);
        unaligned.Should().Throw<ArgumentException>().WithParameterName("capacity");
        Action overflow = () => _ = new QueueOptions("invalid", long.MaxValue - 7);
        overflow.Should().Throw<OverflowException>();
    }

    internal static void RunInitializer(QueueOptions options)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();

        using var coordination = new Mutex(false, "CT3_INIT_" + options.QueueName);
        coordination.WaitOne();
        try
        {
            using var mapping = MemoryMappedFile.CreateOrOpen(
                "CT3_IP_" + options.QueueName, options.GetQueueStorageSize());
            Console.WriteLine("initializing");
            Console.ReadLine();
        }
        finally
        {
            coordination.ReleaseMutex();
        }
    }
}