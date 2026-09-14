```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  IterationCount=8
IterationTime=250ms  LaunchCount=2  WarmupCount=20

```
| Method                                             | Mean     | Error    | StdDev   | Allocated |
|--------------------------------------------------- |---------:|---------:|---------:|----------:|
| &#39;Message enqueue and dequeue - long message&#39;       | 18.81 ns | 0.247 ns | 0.231 ns |         - |
| &#39;Message enqueue and dequeue - ring-wrap workload&#39; | 21.35 ns | 0.078 ns | 0.076 ns |         - |
