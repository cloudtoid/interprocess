```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  IterationCount=8
IterationTime=250ms  LaunchCount=2  WarmupCount=3

```
| Method                                             | Mean     | Error    | StdDev   | Allocated |
|--------------------------------------------------- |---------:|---------:|---------:|----------:|
| &#39;Message enqueue and dequeue - long message&#39;       | 18.01 ns | 0.212 ns | 0.209 ns |         - |
| &#39;Message enqueue and dequeue - ring-wrap workload&#39; | 21.61 ns | 0.215 ns | 0.211 ns |         - |
