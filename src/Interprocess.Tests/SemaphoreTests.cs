using Cloudtoid.Interprocess.Semaphore.Linux;
using FluentAssertions;

namespace Cloudtoid.Interprocess.Tests
{
    public class SemaphoreTests
    {
        [Fact(Platforms = Platform.Linux | Platform.FreeBSD)]
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
        public void CanReleaseAndWaitOSX()
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

        [Fact(Platforms = Platform.Linux | Platform.FreeBSD)]
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
        public void CanCreateMultipleSemaphoresWithSameNameOSX()
        {
            using var sem1 = new SemaphoreLinux("my-sem", deleteOnDispose: true);
            using var sem2 = new SemaphoreLinux("my-sem", deleteOnDispose: false);
            sem2.Release();
            sem1.Wait(10).Should().BeTrue();
            sem1.Wait(10).Should().BeFalse();
            sem2.Wait(10).Should().BeFalse();
        }

        [Fact(Platforms = Platform.Linux | Platform.FreeBSD)]
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
        public void CanReuseSameSemaphoreNameOSX()
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
    }
}
