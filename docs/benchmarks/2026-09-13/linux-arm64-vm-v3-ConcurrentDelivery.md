```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-, 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-SYPGBG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Runtime=.NET 10.0  IterationCount=8  IterationTime=250ms
LaunchCount=2  WarmupCount=3

```
| Method  | PublisherCount | Mean     | Error    | StdDev   |
|-------- |--------------- |---------:|---------:|---------:|
| **Deliver** | **1**              | **217.7 ns** | **13.98 ns** | **13.73 ns** |
| **Deliver** | **4**              | **249.8 ns** | **13.13 ns** | **12.89 ns** |
