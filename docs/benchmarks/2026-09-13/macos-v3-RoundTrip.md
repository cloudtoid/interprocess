```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-SYPGBG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Runtime=.NET 10.0  IterationCount=8  IterationTime=250ms  
LaunchCount=2  WarmupCount=3  

```
| Method         | PayloadBytes | Mean     | Error    | StdDev   | Allocated |
|--------------- |------------- |---------:|---------:|---------:|----------:|
| **SendAndReceive** | **8**            | **32.80 ns** | **0.245 ns** | **0.241 ns** |         **-** |
| **SendAndReceive** | **50**           | **37.88 ns** | **0.911 ns** | **0.895 ns** |         **-** |
