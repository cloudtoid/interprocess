using System.Diagnostics;
using System.Runtime.InteropServices;
using MacInterop = Cloudtoid.Interprocess.Semaphore.MacOS.Interop;

namespace Cloudtoid.Interprocess.Tests;

public sealed class MacSemaphoreTests
{
    [Fact(Platforms = Platform.OSX)]
    public void CreationPreservesInitialCount()
    {
        CheckArchitecture();
        foreach (var count in new[] { 1, 3 })
        {
            var name = NewName();
            var handle = MacInterop.CreateOrOpenSemaphore(name, (uint)count);
            try
            {
                for (var i = 0; i < count; i++)
                    MacInterop.Wait(handle, 0).Should().BeTrue();

                MacInterop.Wait(handle, 0).Should().BeFalse();
            }
            finally
            {
                MacInterop.Close(handle);
                MacInterop.Unlink(name);
            }
        }
    }

    [Fact(Platforms = Platform.OSX)]
    public async Task SeparateProcessesCanOpenAndSignalTheSameSemaphoreAsync()
    {
        CheckArchitecture();
        var name = NewName();
        var handle = MacInterop.CreateOrOpenSemaphore(name, 2);
        try
        {
            foreach (var count in new[] { 2, 1 })
            {
                await RunPeerAsync(name, count);
                MacInterop.Wait(handle, 0).Should().BeTrue("the child posted one notification");
                MacInterop.Wait(handle, 0).Should().BeFalse();
                MacInterop.Release(handle);
            }
        }
        finally
        {
            MacInterop.Close(handle);
            MacInterop.Unlink(name);
        }
    }

    internal static void RunPeer(string name, int expectedCount)
    {
        CheckArchitecture();
        // Opening an existing semaphore must preserve its count, ignoring this creation value.
        var handle = MacInterop.CreateOrOpenSemaphore(name, 7);
        try
        {
            for (var i = 0; i < expectedCount; i++)
                MacInterop.Wait(handle, 0).Should().BeTrue();

            MacInterop.Wait(handle, 0).Should().BeFalse();
            MacInterop.Release(handle);
            Console.WriteLine($"passed {RuntimeInformation.ProcessArchitecture}");
        }
        finally
        {
            MacInterop.Close(handle);
        }
    }

    private static string NewName() => "/ct.ip." + Guid.NewGuid().ToStringInvariant("N")[..16];

    private static void CheckArchitecture()
    {
        var expected = Environment.GetEnvironmentVariable("INTERPROCESS_TEST_ARCHITECTURE");
        if (!string.IsNullOrEmpty(expected))
            RuntimeInformation.ProcessArchitecture.ToString().Should().Be(expected);
    }

    private static async Task RunPeerAsync(string name, int expectedCount)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(MacSemaphoreTests).Assembly.Location);
        start.ArgumentList.Add(Path.GetTempPath());
        start.ArgumentList.Add(name);
        start.ArgumentList.Add("mac-semaphore");
        start.ArgumentList.Add(expectedCount.ToStringInvariant());
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            process.ExitCode.Should().Be(0, await errors);
            (await output).Trim().Should().EndWith($"passed {RuntimeInformation.ProcessArchitecture}");
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
}