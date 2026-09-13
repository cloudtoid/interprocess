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
| Method                                            | Mean     | Error    | StdDev   | Gen0   | Allocated |
|-------------------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| &#39;Message enqueue and dequeue - no message buffer&#39; | 42.49 ns | 0.703 ns | 0.623 ns | 0.0147 |      32 B |
| &#39;Message enqueue and dequeue&#39;                     | 40.43 ns | 0.415 ns | 0.388 ns |      - |         - |
