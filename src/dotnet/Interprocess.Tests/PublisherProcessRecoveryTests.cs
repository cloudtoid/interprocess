using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cloudtoid.Interprocess.Tests;

public sealed partial class PublisherProcessRecoveryTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    private const int BodyLength = 256 * 1024 * 1024;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PausedPublisherMustDieBeforeItsReservationCanBeReusedAsync(bool crash)
    {
        // Catch a real in-flight copy without adding hooks to the publication path.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (await CheckAsync(crash))
                return;
        }

        Assert.Fail("The publisher completed before suspension in all three attempts.");
    }

    internal static void RunChild(QueueOptions original)
    {
        var options = new QueueOptions(original.QueueName, original.Path, BodyLength + 8L);
        using var publisher = new QueueFactory().CreatePublisher(options);
        var body = new byte[BodyLength];
        Array.Fill(body, (byte)90);
        Console.WriteLine(OperatingSystem.IsWindows() ? NativeMethods.GetCurrentThreadId() : 0);
        if (Console.ReadLine() != "publish")
            throw new InvalidOperationException("Expected a publish command.");

        Console.WriteLine(publisher.TryEnqueue(body));
        if (Console.ReadLine() != "exit")
            throw new InvalidOperationException("Expected an exit command.");
    }

    private async Task<bool> CheckAsync(bool crash)
    {
        var options = new QueueOptions(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, BodyLength + 8L);
        var factory = new QueueFactory();
        using var probe = new Probe(options);
        using var survivor = factory.CreateSubscriber(options);
        using var publisher = factory.CreatePublisher(options);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        var arguments = new[] { typeof(Program).Assembly.Location, options.Path, options.QueueName, "publisher-copy" };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var child = Process.Start(start)!;
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            var ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var threadId = uint.Parse(ready!, CultureInfo.InvariantCulture);
            await child.StandardInput.WriteLineAsync("publish");
            await child.StandardInput.FlushAsync();
            SpinWait.SpinUntil(() => probe.Tail != 0, TimeSpan.FromSeconds(5)).Should().BeTrue();
            using var pause = new Suspension(child, threadId);
            await Task.Delay(50); // Allow the Unix stop signal to be delivered.
            if (probe.State != 0)
                return false;

            if (crash)
            {
                child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }

            survivor.TryDequeue(out _).Should().BeFalse();
            await Task.Delay(TimeSpan.FromSeconds(10.1));
            survivor.TryDequeue(out _).Should().BeFalse();
            if (crash)
            {
                probe.Head.Should().Be(probe.Tail);
            }
            else
            {
                probe.Head.Should().Be(0, "a live publisher can still write into its reservation");
                publisher.TryEnqueue("canary!!"u8).Should().BeFalse();
                pause.Dispose();
                (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("True");
                await child.StandardInput.WriteLineAsync("exit");
                await child.StandardInput.FlushAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                child.ExitCode.Should().Be(0, await errors);
                survivor.TryDequeue(out var body).Should().BeTrue();
                body.Length.Should().Be(BodyLength);
                body.Span.IndexOfAnyExcept((byte)90).Should().Be(-1);
            }

            publisher.TryEnqueue("canary!!"u8).Should().BeTrue();
            survivor.TryDequeue(out var canary).Should().BeTrue();
            canary.ToArray().Should().Equal("canary!!"u8.ToArray());
            return true;
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed class Probe(QueueOptions options) : Queue(options, NullLoggerFactory.Instance)
    {
        internal unsafe long Head => Volatile.Read(ref Header->ReadOffset);
        internal unsafe long Tail => Volatile.Read(ref Header->WriteOffset);
        internal unsafe int State => Volatile.Read(ref ((MessageHeader*)Buffer.GetPointer(0))->State);
    }

    private sealed class Suspension : IDisposable
    {
        private readonly Process process;
        private readonly SafeWaitHandle? thread;
        private bool disposed;

        internal Suspension(Process process, uint threadId)
        {
            this.process = process;
            if (OperatingSystem.IsWindows())
            {
                thread = NativeMethods.OpenThread(2, false, threadId); // THREAD_SUSPEND_RESUME
                if (thread.IsInvalid || NativeMethods.SuspendThread(thread) == uint.MaxValue)
                {
                    var error = new Win32Exception();
                    thread.Dispose();
                    throw error;
                }
            }
            else if (NativeMethods.Kill(process.Id, OperatingSystem.IsMacOS() ? 17 : 19) != 0)
            {
                throw new Win32Exception();
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            if (!process.HasExited)
            {
                if (thread is not null)
                    NativeMethods.ResumeThread(thread);
                else
                    NativeMethods.Kill(process.Id, OperatingSystem.IsMacOS() ? 19 : 18);
            }

            thread?.Dispose();
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll")]
        internal static partial uint GetCurrentThreadId();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial SafeWaitHandle OpenThread(
            uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial uint SuspendThread(SafeWaitHandle thread);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial uint ResumeThread(SafeWaitHandle thread);

        [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
        internal static partial int Kill(int pid, int signal);
    }
}