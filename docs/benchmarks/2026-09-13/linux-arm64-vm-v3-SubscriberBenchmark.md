```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
-, 4 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=ShortRun  IterationCount=8  IterationTime=250ms
LaunchCount=2  WarmupCount=3

```
| Method                   | SubscriberCount | Mean     | Error    | StdDev   |
|------------------------- |---------------- |---------:|---------:|---------:|
| **ReceiveConcurrentlyAsync** | **1**               | **107.7 ns** |  **1.76 ns** |  **1.73 ns** |
| **ReceiveConcurrentlyAsync** | **4**               | **161.0 ns** | **13.57 ns** | **13.33 ns** |
