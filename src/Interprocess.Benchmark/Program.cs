using BenchmarkDotNet.Running;

namespace Cloudtoid.Interprocess.Benchmark;

public sealed class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}