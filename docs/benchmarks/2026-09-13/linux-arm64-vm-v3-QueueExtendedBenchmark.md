```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Affinity=0001  Runtime=.NET 10.0
IterationCount=8  IterationTime=250ms  LaunchCount=2
WarmupCount=20

```
| Method                                             | Mean     | Error   | StdDev  | Allocated |
|--------------------------------------------------- |---------:|--------:|--------:|----------:|
| &#39;Message enqueue and dequeue - long message&#39;       | 156.1 ns | 4.26 ns | 3.78 ns |         - |
| &#39;Message enqueue and dequeue - ring-wrap workload&#39; | 177.6 ns | 3.38 ns | 3.16 ns |         - |
