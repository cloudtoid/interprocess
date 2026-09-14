```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9445/25H2/2025Update/HudsonValley2)
virt-10.0 1.00GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=ShortRun  IterationCount=8  IterationTime=250ms
LaunchCount=2  WarmupCount=3

```
| Method                   | SubscriberCount | Mean      | Error     | StdDev    |
|------------------------- |---------------- |----------:|----------:|----------:|
| **ReceiveConcurrentlyAsync** | **1**               | **101.30 ns** |  **1.713 ns** |  **1.682 ns** |
| **ReceiveConcurrentlyAsync** | **4**               |  **85.62 ns** | **12.725 ns** | **12.497 ns** |
