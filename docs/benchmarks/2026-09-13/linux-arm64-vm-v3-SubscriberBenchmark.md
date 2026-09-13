```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=ShortRun  IterationCount=8  IterationTime=250ms
LaunchCount=2  WarmupCount=3

```
| Method                   | SubscriberCount | Mean     | Error    | StdDev   |
|------------------------- |---------------- |---------:|---------:|---------:|
| **ReceiveConcurrentlyAsync** | **1**               | **111.0 ns** |  **0.70 ns** |  **0.68 ns** |
| **ReceiveConcurrentlyAsync** | **4**               | **175.1 ns** | **17.94 ns** | **17.62 ns** |
