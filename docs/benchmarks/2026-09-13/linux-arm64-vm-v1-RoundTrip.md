```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-FGQHFY : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Affinity=0001  Runtime=.NET 10.0  IterationCount=8
IterationTime=250ms  LaunchCount=2  WarmupCount=20

```
| Method         | PayloadBytes | Mean     | Error     | StdDev    | Allocated |
|--------------- |------------- |---------:|----------:|----------:|----------:|
| **SendAndReceive** | **8**            | **35.20 ns** |  **0.511 ns** |  **0.502 ns** |         **-** |
| **SendAndReceive** | **50**           | **81.37 ns** | **41.403 ns** | **40.664 ns** |         **-** |
