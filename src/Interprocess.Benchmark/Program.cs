using BenchmarkDotNet.Running;

namespace Cloudtoid.Interprocess.Benchmark
{
    public sealed class Program
    {
        static void Main()
        {
            _ = BenchmarkRunner.Run(typeof(Program).Assembly);
        }
    }
}
