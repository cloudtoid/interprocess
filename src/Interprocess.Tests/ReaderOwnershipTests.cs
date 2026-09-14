using System.Buffers;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace Cloudtoid.Interprocess.Tests;

public sealed class ReaderOwnershipTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    [Fact]
    public void RegistrationsAreNotReusedWhileTheQueueIsAlive()
    {
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 64);
        using var probe = new OwnershipProbe(options);
        var factory = new QueueFactory();
        for (var id = 1; id <= 32; id++)
        {
            using (var subscriber = factory.CreateSubscriber(options))
            {
                probe.LastParticipantId.Should().Be(id);
                ReaderLease.IsAlive(options, id).Should().BeTrue();
            }

            ReaderLease.IsAlive(options, id).Should().BeFalse();
        }
    }

    [Fact]
    public void RegistrationExhaustionDoesNotWrapOrBreakExistingParticipants()
    {
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 64);
        var factory = new QueueFactory();
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var probe = new OwnershipProbe(options);
        probe.LastParticipantId = int.MaxValue;
        Assert.Throws<OverflowException>(() => factory.CreateSubscriber(options));
        probe.LastParticipantId.Should().Be(int.MaxValue);
        publisher.TryEnqueue("survivor"u8).Should().BeTrue();
        subscriber.TryDequeue(out var message).Should().BeTrue();
        message.ToArray().Should().Equal("survivor"u8.ToArray());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task ReaderProcessLifetimeControlsRecoveryAsync(bool crash, bool admissionClosed, bool emptied)
    {
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 1024);
        var factory = new QueueFactory();
        using var publisher = factory.CreatePublisher(options);
        using var survivor = factory.CreateSubscriber(options);
        using var probe = new OwnershipProbe(options);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(ReaderOwnershipTests).Assembly.Location);
        start.ArgumentList.Add(options.Path);
        start.ArgumentList.Add(options.QueueName);
        start.ArgumentList.Add("reader-ownership");
        using var child = Process.Start(start)!;
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("ready");
            publisher.TryEnqueue("first!!!"u8).Should().BeTrue();
            await child.StandardInput.WriteLineAsync("read");
            await child.StandardInput.FlushAsync();
            (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("locked");
            var owner = probe.Owner;
            owner.Should().NotBe(0);
            // Another probe can keep the registration mapping open after the owner process dies.
            using var keptRegistration = OperatingSystem.IsWindows()
                ? MemoryMappedFile.OpenExisting("CT3_READER_" + options.QueueName + "." + owner.ToStringInvariant())
                : null;

            if (crash)
            {
                if (admissionClosed)
                    probe.CloseAdmission();

                if (emptied)
                    probe.EmptyQueue();

                child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (probe.ReadOffset == 0 || probe.Owner != 0)
                {
                    survivor.TryDequeue(out _).Should().BeFalse("the crashed read cannot be delivered again");
                    await Task.Delay(10, timeout.Token);
                }
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(10.1));
                survivor.TryDequeue(out _).Should().BeFalse();
                probe.Owner.Should().Be(owner, "the other process is paused, not dead");
                await child.StandardInput.WriteLineAsync("resume");
                await child.StandardInput.FlushAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                child.ExitCode.Should().Be(0, await errors);
            }

            probe.ReadOffset.Should().Be(16);
            for (var i = 0; i < 256; i++)
            {
                publisher.TryEnqueue("survivor"u8).Should().BeTrue();
                survivor.TryDequeue(out var message).Should().BeTrue();
                message.ToArray().Should().Equal("survivor"u8.ToArray());
            }

            survivor.TryDequeue(out _).Should().BeFalse();
            probe.Owner.Should().Be(0);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill();
                await child.WaitForExitAsync();
            }
        }
    }

    [Fact]
    public async Task DisposalKeepsRegistrationUntilActiveReadFinishesAsync()
    {
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 64);
        var factory = new QueueFactory();
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var successor = factory.CreateSubscriber(options);
        using var probe = new OwnershipProbe(options);
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var memory = new PausedMemory(entered, resume);
        var buffer = memory.Memory;
        memory.Pause = true;
        publisher.TryEnqueue("first!!!"u8).Should().BeTrue();
        publisher.TryEnqueue("second!!"u8).Should().BeTrue();
        var read = Task.Factory.StartNew(
            () => subscriber.Dequeue(buffer, default),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Task? dispose = null;
        long owner = 0;
        try
        {
            entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            owner = probe.Owner;
            dispose = Task.Run(subscriber.Dispose);
            // Wait until disposal has closed admission while the original read is still paused.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                try
                {
                    subscriber.TryDequeue(out _).Should().BeFalse();
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await Task.Delay(1, timeout.Token);
            }

            dispose.IsCompleted.Should().BeFalse();
            ReaderLease.IsAlive(options, owner).Should().BeTrue();
            successor.TryDequeue(out _).Should().BeFalse();
        }
        finally
        {
            resume.Set();
            await read.WaitAsync(TimeSpan.FromSeconds(5));
            if (dispose is not null)
                await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        }

        (await read).ToArray().Should().Equal("first!!!"u8.ToArray());
        ReaderLease.IsAlive(options, owner).Should().BeFalse();
        successor.TryDequeue(out var second).Should().BeTrue();
        second.ToArray().Should().Equal("second!!"u8.ToArray());
    }

    [Fact]
    public async Task PausedReaderKeepsOwnershipAfterTimeoutAsync()
    {
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 64);
        var factory = new QueueFactory();
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var successor = factory.CreateSubscriber(options);
        using var probe = new OwnershipProbe(options);
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var memory = new PausedMemory(entered, resume);
        var buffer = memory.Memory;
        memory.Pause = true;
        publisher.TryEnqueue("first!!!"u8).Should().BeTrue();
        publisher.TryEnqueue("second!!"u8).Should().BeTrue();

        var read = Task.Factory.StartNew(
            () => subscriber.Dequeue(buffer, default),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        try
        {
            entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            var owner = probe.Owner;
            owner.Should().NotBe(0);
            // Cross the real recovery deadline while the owner is paused inside a public read.
            await Task.Delay(TimeSpan.FromSeconds(10.1));
            successor.TryDequeue(out _).Should().BeFalse();
            probe.Owner.Should().Be(owner, "a timeout does not prove that the reader has died");
            probe.ReadOffset.Should().Be(0);
        }
        finally
        {
            resume.Set();
            await read.WaitAsync(TimeSpan.FromSeconds(5));
        }

        (await read).ToArray().Should().Equal("first!!!"u8.ToArray());
        successor.TryDequeue(out var second).Should().BeTrue();
        second.ToArray().Should().Equal("second!!"u8.ToArray());
        for (var i = 0; i < 16; i++)
        {
            publisher.TryEnqueue("next-lap"u8).Should().BeTrue();
            successor.TryDequeue(out var message).Should().BeTrue();
            message.ToArray().Should().Equal("next-lap"u8.ToArray());
        }

        successor.TryDequeue(out _).Should().BeFalse();
        probe.Owner.Should().Be(0);
    }

    internal static void RunChild(QueueOptions options)
    {
        using var subscriber = new QueueFactory().CreateSubscriber(options);
        using var memory = new ChildMemory();
        var buffer = memory.Memory;
        memory.Pause = true;
        Console.WriteLine("ready");
        if (Console.ReadLine() != "read")
            throw new InvalidOperationException("Expected a read command.");

        if (!subscriber.Dequeue(buffer, default).Span.SequenceEqual("first!!!"u8))
            throw new InvalidOperationException("The paused read returned the wrong message.");
    }

    private sealed class OwnershipProbe(QueueOptions options) : Queue(options, NullLoggerFactory.Instance)
    {
        internal unsafe long Owner => Volatile.Read(ref Header->ReadLockOwner);
        internal unsafe long ReadOffset => Volatile.Read(ref Header->ReadOffset);
        internal unsafe int LastParticipantId
        {
            get => Volatile.Read(ref Header->LastParticipantId);
            set => Volatile.Write(ref Header->LastParticipantId, value);
        }

        internal void CloseAdmission() => Publishers.CloseAdmission();

        internal unsafe void EmptyQueue()
        {
            Buffer.Clear(Header->ReadOffset, Header->WriteOffset - Header->ReadOffset);
            Interlocked.Exchange(ref Header->ReadOffset, Header->WriteOffset);
        }
    }

    private sealed class PausedMemory(ManualResetEventSlim entered, ManualResetEventSlim resume) : MemoryManager<byte>
    {
        private readonly byte[] bytes = new byte[8];
        internal bool Pause { get; set; }

        public override Span<byte> GetSpan()
        {
            if (Pause)
            {
                entered.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("The paused reader was not resumed.");
            }

            return bytes;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }

    private sealed class ChildMemory : MemoryManager<byte>
    {
        private readonly byte[] bytes = new byte[8];
        internal bool Pause { get; set; }

        public override Span<byte> GetSpan()
        {
            if (Pause)
            {
                Console.WriteLine("locked");
                if (Console.ReadLine() != "resume")
                    throw new InvalidOperationException("Expected a resume command.");

                Pause = false;
            }

            return bytes;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}