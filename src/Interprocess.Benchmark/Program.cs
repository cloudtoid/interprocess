using System.Threading;
using Cloudtoid.Interprocess.Semaphore.Linux;
using FluentAssertions;

namespace Cloudtoid.Interprocess.Benchmark
{
    public sealed class Program
    {
        public static void Main()
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
    }
}
