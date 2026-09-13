```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-SYPGBG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Runtime=.NET 10.0  IterationCount=8  IterationTime=250ms  
LaunchCount=2  WarmupCount=3  

```
| Method  | PublisherCount | Mean     | Error    | StdDev   |
|-------- |--------------- |---------:|---------:|---------:|
| **Deliver** | **1**              | **322.0 ns** | **23.41 ns** | **22.99 ns** |
| **Deliver** | **4**              | **467.1 ns** | **40.26 ns** | **37.66 ns** |
