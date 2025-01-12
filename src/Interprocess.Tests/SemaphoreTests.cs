using Cloudtoid.Interprocess.Semaphore.Linux;
using Cloudtoid.Interprocess.Semaphore.MacOS;
using FluentAssertions;

namespace Cloudtoid.Interprocess.Tests;

public class SemaphoreTests
{
    [Fact(Platforms = Platform.Linux | Platform.FreeBSD)]
    [TestBeforeAfter]
    public void CanReleaseAndWaitLinux()
    {
        using var sem = new SemaphoreLinux("my-sem", deleteOnDispose: true);
        sem.Wait(10).Should().BeFalse();
        sem.Release();
        sem.Release();
        sem.Wait(-1).Should().BeTrue();
        sem.Wait(10).Should().BeTrue();
        sem.Wait(0).Should().BeFalse();
        sem.Wait(10).Should().BeFalse();
        sem.Release();
        sem.Wait(10).Should().BeTrue();
    }

    [Fact(Platforms = Platform.OSX)]
    [TestBeforeAfter]
    public void CanReleaseAndWaitMacOS()
    {
        using var sem = new SemaphoreMacOS("my-sem", deleteOnDispose: true);
        sem.Wait(10).Should().BeFalse();
        sem.Release();
        sem.Release();
        sem.Wait(-1).Should().BeTrue();
        sem.Wait(10).Should().BeTrue();
        sem.Wait(0).Should().BeFalse();
        sem.Wait(10).Should().BeFalse();
        sem.Release();
        sem.Wait(10).Should().BeTrue();
    }

    [Fact(Platforms = Platform.Linux | Platform.FreeBSD)]
    [TestBeforeAfter]
    public void CanCreateMultipleSemaphoresWithSameNameLinux()
    {
        using var sem1 = new SemaphoreLinux("my-sem", deleteOnDispose: true);
        using var sem2 = new SemaphoreLinux("my-sem", deleteOnDispose: false);
        sem2.Release();
        sem1.Wait(10).Should().BeTrue();
        sem1.Wait(10).Should().BeFalse();
        sem2.Wait(10).Should().BeFalse();
    }

    [Fact(Platforms = Platform.OSX)]
    [TestBeforeAfter]
    public void CanCreateMultipleSemaphoresWithSameNameMacOS()
    {
        using var sem1 = new SemaphoreMacOS("my-sem", deleteOnDispose: true);
        using var sem2 = new SemaphoreMacOS("my-sem", deleteOnDispose: false);
        sem2.Release();
        sem1.Wait(10).Should().BeTrue();
        sem1.Wait(10).Should().BeFalse();
        sem2.Wait(10).Should().BeFalse();
    }

    [Fact(Platforms = Platform.Linux | Platform.FreeBSD)]
    [TestBeforeAfter]
    public void CanReuseSameSemaphoreNameLinux()
    {
        using (var sem = new SemaphoreLinux("my-sem", deleteOnDispose: true))
        {
            sem.Wait(10).Should().BeFalse();
            sem.Release();
            sem.Wait(-1).Should().BeTrue();
            sem.Release();
        }

        using (var sem = new SemaphoreLinux("my-sem", deleteOnDispose: false))
        {
            sem.Wait(10).Should().BeFalse();
            sem.Release();
            sem.Wait(-1).Should().BeTrue();
            sem.Release();
        }

        using (var sem = new SemaphoreLinux("my-sem", deleteOnDispose: true))
        {
            sem.Wait(10).Should().BeTrue();
            sem.Release();
            sem.Wait(-1).Should().BeTrue();
            sem.Release();
        }
    }

    [Fact(Platforms = Platform.OSX)]
    [TestBeforeAfter]
    public void CanReuseSameSemaphoreNameMacOS()
    {
        using (var sem = new SemaphoreMacOS("my-sem", deleteOnDispose: true))
        {
            sem.Wait(10).Should().BeFalse();
            sem.Release();
            sem.Wait(-1).Should().BeTrue();
            sem.Release();
        }

        using (var sem = new SemaphoreMacOS("my-sem", deleteOnDispose: false))
        {
            sem.Wait(10).Should().BeFalse();
            sem.Release();
            sem.Wait(-1).Should().BeTrue();
            sem.Release();
        }

        using (var sem = new SemaphoreMacOS("my-sem", deleteOnDispose: true))
        {
            sem.Wait(10).Should().BeTrue();
            sem.Release();
            sem.Wait(-1).Should().BeTrue();
            sem.Release();
        }
    }
}