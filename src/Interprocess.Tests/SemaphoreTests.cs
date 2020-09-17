using Cloudtoid.Interprocess.Semaphore.Linux;
using Cloudtoid.Interprocess.Semaphore.OSX;
using FluentAssertions;

namespace Cloudtoid.Interprocess.Tests
{
    public class SemaphoreTests
    {
        [Fact(Platforms = Platform.Linux | Platform.FreeBSD, Skip = "Ignore")]
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
        public void CanReleaseAndWaitOSX()
        {
            using var sem = new SemaphoreOSX("my-sem", deleteOnDispose: true);
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

        [Fact(Platforms = Platform.Linux | Platform.FreeBSD, Skip = "Ignore")]
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
        public void CanCreateMultipleSemaphoresWithSameNameOSX()
        {
            using var sem1 = new SemaphoreOSX("my-sem", deleteOnDispose: true);
            using var sem2 = new SemaphoreOSX("my-sem", deleteOnDispose: false);
            sem2.Release();
            sem1.Wait(10).Should().BeTrue();
            sem1.Wait(10).Should().BeFalse();
            sem2.Wait(10).Should().BeFalse();
        }

        [Fact(Platforms = Platform.Linux | Platform.FreeBSD, Skip = "Ignore")]
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
        public void CanReuseSameSemaphoreNameOSX()
        {
            using (var sem = new SemaphoreOSX("my-sem", deleteOnDispose: true))
            {
                sem.Wait(10).Should().BeFalse();
                sem.Release();
                sem.Wait(-1).Should().BeTrue();
                sem.Release();
            }

            using (var sem = new SemaphoreOSX("my-sem", deleteOnDispose: false))
            {
                sem.Wait(10).Should().BeFalse();
                sem.Release();
                sem.Wait(-1).Should().BeTrue();
                sem.Release();
            }

            using (var sem = new SemaphoreOSX("my-sem", deleteOnDispose: true))
            {
                sem.Wait(10).Should().BeTrue();
                sem.Release();
                sem.Wait(-1).Should().BeTrue();
                sem.Release();
            }
        }
    }
}
