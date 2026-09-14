```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-XTFAQF : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Runtime=.NET 10.0  IterationCount=8  IterationTime=250ms
LaunchCount=2  WarmupCount=20

```
| Method  | PublisherCount | Mean     | Error    | StdDev   |
|-------- |--------------- |---------:|---------:|---------:|
| **Deliver** | **1**              | **392.8 ns** |  **9.43 ns** |  **9.26 ns** |
| **Deliver** | **4**              | **961.8 ns** | **23.84 ns** | **23.42 ns** |
