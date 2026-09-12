# Native macOS benchmark measurements — September 12, 2026

All seven benchmark cases were run directly on an Apple M5 Max using benchmark and library source at [`c7d0755`](https://github.com/cloudtoid/interprocess/commit/c7d07559a48045c82b63940da9f7910b888d9e82). The later README update does not change the measured code.

[Complete BenchmarkDotNet reports](macos-local.md)

## Environment

- macOS Tahoe 26.6.2 (25G83), Darwin 25.6.0.
- Apple M5 Max, arm64, 18 logical and physical cores reported.
- BenchmarkDotNet 0.15.8; .NET SDK 10.0.401; .NET runtime 10.0.12; Release configuration.
- Three warm-up iterations and eight measured iterations per case, one launch, with BenchmarkDotNet's usual pilot, overhead, and outlier handling.
- These are in-process microbenchmarks. They do not measure end-to-end latency between separate applications, idle CPU usage, or latency percentiles.

## Workloads

- **Enqueue:** 320,000 three-byte messages per invocation, normalized to one enqueue. Queue capacity is 5,120,000 bytes. Draining and validation happen outside the timed body. Each enqueue checks that it succeeded.
- **Three-byte round trips:** enqueue followed by dequeue on the calling thread, with either a reused buffer or a newly allocated result array; queue capacity 128 bytes.
- **50-byte round trips:** the same calling-thread pattern with a reused buffer and a 128-byte queue.
- **Ring-wrap workload:** 50-byte messages in a 120-byte queue, so padded 64-byte records repeatedly cross the end of the ring. Each invocation performs two enqueue/dequeue pairs; reported time is normalized to one pair.
- **Concurrent delivery:** one publisher sends 65,536 eight-byte messages to one or four dedicated subscriber threads sharing a 65,536-byte queue. Each subscriber receives an equal share. Time is normalized per delivered message and includes per-batch worker startup and completion.

Memory diagnostics reported 0 B/op for enqueue and reused-buffer round trips, and 32 B/op for the three-byte result-array case. Allocation diagnostics were not enabled for concurrent delivery, which creates workers and tasks per batch.

## Reproduce

From the repository root:

```sh
dotnet run --project src/Interprocess.Benchmark -c Release -- --filter '*' --warmupCount 3 --iterationCount 8 --artifacts BenchmarkDotNet.Artifacts
```
