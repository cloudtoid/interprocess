```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-, 4 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Affinity=0001  Runtime=.NET 10.0
InvocationCount=1  IterationCount=8  LaunchCount=2
UnrollFactor=1  WarmupCount=200

```
| Method            | Mean     | Error     | StdDev    | Allocated |
|------------------ |---------:|----------:|----------:|----------:|
| &#39;Message enqueue&#39; | 5.050 ns | 0.0774 ns | 0.0686 ns |         - |
