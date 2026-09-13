```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  IterationCount=8
IterationTime=250ms  LaunchCount=2  WarmupCount=3

```
| Method                                            | Mean     | Error    | StdDev   | Gen0   | Allocated |
|-------------------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| &#39;Message enqueue and dequeue - no message buffer&#39; | 19.85 ns | 0.238 ns | 0.233 ns | 0.0037 |      32 B |
| &#39;Message enqueue and dequeue&#39;                     | 17.27 ns | 0.155 ns | 0.152 ns |      - |         - |
