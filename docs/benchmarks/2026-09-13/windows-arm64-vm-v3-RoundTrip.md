```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8037/25H2/2025Update/HudsonValley2)
virt-10.0 1.00GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-FGQHFY : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Affinity=0001  Runtime=.NET 10.0  IterationCount=8
IterationTime=250ms  LaunchCount=2  WarmupCount=20

```
| Method         | PayloadBytes | Mean     | Error    | StdDev   | Allocated |
|--------------- |------------- |---------:|---------:|---------:|----------:|
| **SendAndReceive** | **8**            | **41.84 ns** | **1.346 ns** | **1.322 ns** |         **-** |
| **SendAndReceive** | **50**           | **43.44 ns** | **1.313 ns** | **1.228 ns** |         **-** |
