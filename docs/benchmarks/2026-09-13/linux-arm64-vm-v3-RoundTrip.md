```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-, 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-FGQHFY : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Affinity=0001  Runtime=.NET 10.0  IterationCount=8
IterationTime=250ms  LaunchCount=2  WarmupCount=20

```
| Method         | PayloadBytes | Mean     | Error    | StdDev   | Allocated |
|--------------- |------------- |---------:|---------:|---------:|----------:|
| **SendAndReceive** | **8**            | **18.29 ns** | **0.120 ns** | **0.112 ns** |         **-** |
| **SendAndReceive** | **50**           | **20.79 ns** | **0.331 ns** | **0.309 ns** |         **-** |
