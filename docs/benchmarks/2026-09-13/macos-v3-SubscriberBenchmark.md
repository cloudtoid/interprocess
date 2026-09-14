```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=ShortRun  IterationCount=8  IterationTime=250ms
LaunchCount=2  WarmupCount=3

```
| Method                   | SubscriberCount | Mean     | Error    | StdDev   |
|------------------------- |---------------- |---------:|---------:|---------:|
| **ReceiveConcurrentlyAsync** | **1**               | **113.4 ns** |  **1.02 ns** |  **0.95 ns** |
| **ReceiveConcurrentlyAsync** | **4**               | **155.3 ns** | **11.40 ns** | **11.19 ns** |
