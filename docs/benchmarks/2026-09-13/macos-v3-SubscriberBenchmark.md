```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=ShortRun  IterationCount=8  IterationTime=250ms
LaunchCount=2  WarmupCount=3

```
| Method                   | SubscriberCount | Mean     | Error   | StdDev  |
|------------------------- |---------------- |---------:|--------:|--------:|
| **ReceiveConcurrentlyAsync** | **1**               | **104.9 ns** | **1.20 ns** | **1.17 ns** |
| **ReceiveConcurrentlyAsync** | **4**               | **139.7 ns** | **6.88 ns** | **6.43 ns** |
