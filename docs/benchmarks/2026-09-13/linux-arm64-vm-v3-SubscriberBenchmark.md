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
| **ReceiveConcurrentlyAsync** | **1**               | **142.6 ns** |  **1.99 ns** |  **1.86 ns** |
| **ReceiveConcurrentlyAsync** | **4**               | **221.1 ns** | **20.30 ns** | **19.94 ns** |
