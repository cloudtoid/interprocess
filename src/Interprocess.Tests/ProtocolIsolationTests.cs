using System.IO.MemoryMappedFiles;
using LinuxInterop = Cloudtoid.Interprocess.Semaphore.Linux.Interop;
using MacInterop = Cloudtoid.Interprocess.Semaphore.MacOS.Interop;
using SysSemaphore = System.Threading.Semaphore;

namespace Cloudtoid.Interprocess.Tests;

public sealed class ProtocolIsolationTests(UniquePathFixture fixture) : IClassFixture<UniquePathFixture>
{
    private readonly QueueOptions options = new(Guid.NewGuid().ToStringInvariant("N")[..16], fixture.Path, 64);

    [Fact(Platforms = Platform.Linux | Platform.OSX)]
    public void LegacyUnixResourcesAreUntouched()
    {
        var directory = Path.Combine(options.Path, ".cloudtoid/interprocess/mmf");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, options.QueueName + ".qu");
        var original = Enumerable.Repeat((byte)0xA5, 96).ToArray();
        File.WriteAllBytes(file, original);
        var name = "/ct.ip." + options.QueueName;
        var mac = OperatingSystem.IsMacOS();
        var handle = mac
            ? MacInterop.CreateOrOpenSemaphore(name, 1)
            : LinuxInterop.CreateOrOpenSemaphore(name, 1);
        try
        {
            RoundTrip();
            File.ReadAllBytes(file).Should().Equal(original);
            (mac ? MacInterop.Wait(handle, 0) : LinuxInterop.Wait(handle, 0)).Should().BeTrue();
            (mac ? MacInterop.Wait(handle, 0) : LinuxInterop.Wait(handle, 0)).Should().BeFalse();

            // Cleanup of v3 must not unlink the legacy semaphore either.
            var reopened = mac
                ? MacInterop.CreateOrOpenSemaphore(name, 7)
                : LinuxInterop.CreateOrOpenSemaphore(name, 7);
            try
            {
                (mac ? MacInterop.Wait(reopened, 0) : LinuxInterop.Wait(reopened, 0)).Should().BeFalse();
            }
            finally
            {
                if (mac)
                    MacInterop.Close(reopened);
                else
                    LinuxInterop.Close(reopened);
            }
        }
        finally
        {
            if (mac)
            {
                MacInterop.Close(handle);
                MacInterop.Unlink(name);
            }
            else
            {
                LinuxInterop.Close(handle);
                LinuxInterop.Unlink(name);
            }

            File.Delete(file);
        }
    }

    [Fact(Platforms = Platform.Windows)]
    public void LegacyWindowsResourcesAreUntouched()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var mapping = MemoryMappedFile.CreateOrOpen("CT_IP_" + options.QueueName, 96);
        using var view = mapping.CreateViewAccessor();
        using var signal = new SysSemaphore(1, int.MaxValue, @"Global\CT.IP." + options.QueueName);
        view.Write(95, (byte)0xA5);

        RoundTrip();

        view.ReadInt64(0).Should().Be(0);
        view.ReadInt64(8).Should().Be(0);
        view.ReadByte(95).Should().Be(0xA5);
        signal.WaitOne(0).Should().BeTrue();
        signal.WaitOne(0).Should().BeFalse();
    }

    private void RoundTrip()
    {
        var factory = new QueueFactory();
        using var publisher = factory.CreatePublisher(options);
        using var subscriber = factory.CreateSubscriber(options);
        using var signal = InterprocessSemaphore.CreateWaiter(options.QueueName);
        signal.Wait(0).Should().BeFalse("v3 must not consume legacy notifications");
        for (var i = 0; i < 20; i++)
        {
            publisher.TryEnqueue("v3-body!"u8).Should().BeTrue();
            signal.Wait(0).Should().BeTrue();
            subscriber.TryDequeue(out var message).Should().BeTrue();
            message.ToArray().Should().Equal("v3-body!"u8.ToArray());
        }

        subscriber.TryDequeue(out _).Should().BeFalse();
    }
}