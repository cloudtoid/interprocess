# Local macOS — Apple M5 Max

Measured September 12, 2026, using source commit [`c7d0755`](https://github.com/cloudtoid/interprocess/commit/c7d07559a48045c82b63940da9f7910b888d9e82).

See [methodology and reproduction commands](README.md). The tables below are BenchmarkDotNet exports.

## EnqueueBenchmark

```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  InvocationCount=1  
IterationCount=8  UnrollFactor=1  WarmupCount=3  

```
| Method            | Mean     | Error   | StdDev  | Allocated |
|------------------ |---------:|--------:|--------:|----------:|
| &#39;Message enqueue&#39; | 182.3 ns | 9.57 ns | 5.01 ns |         - |

## QueueBenchmark

```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  IterationCount=8  
WarmupCount=3  

```
| Method                                            | Mean     | Error   | StdDev  | Gen0   | Allocated |
|-------------------------------------------------- |---------:|--------:|--------:|-------:|----------:|
| &#39;Message enqueue and dequeue - no message buffer&#39; | 214.9 ns | 1.54 ns | 0.81 ns | 0.0038 |      32 B |
| &#39;Message enqueue and dequeue&#39;                     | 210.0 ns | 1.03 ns | 0.46 ns |      - |         - |

## QueueExtendedBenchmark

```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]    : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  .NET 10.0 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=.NET 10.0  Runtime=.NET 10.0  IterationCount=8  
WarmupCount=3  

```
| Method                                             | Mean     | Error   | StdDev  | Allocated |
|--------------------------------------------------- |---------:|--------:|--------:|----------:|
| &#39;Message enqueue and dequeue - long message&#39;       | 214.6 ns | 2.55 ns | 1.33 ns |         - |
| &#39;Message enqueue and dequeue - ring-wrap workload&#39; | 223.8 ns | 2.43 ns | 1.08 ns |         - |

## SubscriberBenchmark

```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Job=ShortRun  IterationCount=8  LaunchCount=1  
WarmupCount=3  

```
| Method                   | SubscriberCount | Mean     | Error   | StdDev  |
|------------------------- |---------------- |---------:|--------:|--------:|
| **ReceiveConcurrentlyAsync** | **1**               | **246.5 ns** | **4.21 ns** | **2.20 ns** |
| **ReceiveConcurrentlyAsync** | **4**               | **344.7 ns** | **3.45 ns** | **1.81 ns** |
