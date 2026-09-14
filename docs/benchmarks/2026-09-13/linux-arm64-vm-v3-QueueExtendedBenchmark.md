```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-, 4 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Affinity=0001  Runtime=.NET 10.0
IterationCount=8  IterationTime=250ms  LaunchCount=2
WarmupCount=20

```
| Method                                             | Mean     | Error    | StdDev   | Allocated |
|--------------------------------------------------- |---------:|---------:|---------:|----------:|
| &#39;Message enqueue and dequeue - long message&#39;       | 17.64 ns | 0.216 ns | 0.212 ns |         - |
| &#39;Message enqueue and dequeue - ring-wrap workload&#39; | 20.82 ns | 0.120 ns | 0.112 ns |         - |
