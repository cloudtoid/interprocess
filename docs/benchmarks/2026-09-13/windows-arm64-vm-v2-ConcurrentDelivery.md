```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8037/25H2/2025Update/HudsonValley2)
virt-10.0 1.00GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a
  Job-SYPGBG : .NET 10.0.12 (10.0.12, 10.0.1226.42308), Arm64 RyuJIT armv8.0-a

Runtime=.NET 10.0  IterationCount=8  IterationTime=250ms
LaunchCount=2  WarmupCount=3

```
| Method  | PublisherCount | Mean     | Error     | StdDev    |
|-------- |--------------- |---------:|----------:|----------:|
| **Deliver** | **1**              | **3.935 μs** | **1.4615 μs** | **1.4354 μs** |
| **Deliver** | **4**              | **1.564 μs** | **0.3119 μs** | **0.2918 μs** |
