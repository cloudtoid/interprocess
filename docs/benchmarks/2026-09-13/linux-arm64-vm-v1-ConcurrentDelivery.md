```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-SYPGBG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Runtime=.NET 10.0  IterationCount=8  IterationTime=250ms
LaunchCount=2  WarmupCount=3

```
| Method  | PublisherCount | Mean     | Error   | StdDev  |
|-------- |--------------- |---------:|--------:|--------:|
| **Deliver** | **1**              |       **NA** |      **NA** |      **NA** |
| **Deliver** | **4**              | **125.1 ns** | **1.76 ns** | **1.65 ns** |

Benchmarks with issues:
  ConcurrentDelivery.Deliver: Job-SYPGBG(Runtime=.NET 10.0, IterationCount=8, IterationTime=250ms, LaunchCount=2, WarmupCount=3) [PublisherCount=1]
