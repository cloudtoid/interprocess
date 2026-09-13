```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8037/25H2/2025Update/HudsonValley2)
virt-10.0 1.00GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Affinity=0001  Runtime=.NET 10.0
IterationCount=8  IterationTime=250ms  LaunchCount=2
WarmupCount=20

```
| Method                                             | Mean     | Error    | StdDev   | Allocated |
|--------------------------------------------------- |---------:|---------:|---------:|----------:|
| &#39;Message enqueue and dequeue - long message&#39;       | 41.64 ns | 0.682 ns | 0.670 ns |         - |
| &#39;Message enqueue and dequeue - ring-wrap workload&#39; | 44.80 ns | 1.012 ns | 0.994 ns |         - |
