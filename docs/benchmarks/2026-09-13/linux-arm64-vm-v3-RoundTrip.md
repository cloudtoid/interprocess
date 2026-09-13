```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-FGQHFY : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Affinity=0001  Runtime=.NET 10.0  IterationCount=8
IterationTime=250ms  LaunchCount=2  WarmupCount=20

```
| Method         | PayloadBytes | Mean     | Error   | StdDev  | Allocated |
|--------------- |------------- |---------:|--------:|--------:|----------:|
| **SendAndReceive** | **8**            | **147.2 ns** | **4.31 ns** | **4.03 ns** |         **-** |
| **SendAndReceive** | **50**           | **163.3 ns** | **2.13 ns** | **1.89 ns** |         **-** |
