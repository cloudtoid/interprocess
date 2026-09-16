[<img src="https://raw.githubusercontent.com/cloudtoid/assets/master/logos/cloudtoid-blue.svg" width="100px">][Cloudtoid]

# Interprocess

[Website](https://cloudtoid.com) · [Documentation](https://cloudtoid.com/docs/) · [Languages and packages](#languages) · [Quick start](https://cloudtoid.com/docs/) · [Performance](#performance) · [Protocol v3](https://cloudtoid.com/docs/protocol/)

[![NuGet](https://img.shields.io/nuget/v/Cloudtoid.Interprocess?label=NuGet)](https://www.nuget.org/packages/Cloudtoid.Interprocess)
[![Rust](https://img.shields.io/crates/v/cloudtoid-interprocess?label=Rust)](https://crates.io/crates/cloudtoid-interprocess)
[![C FFI](https://img.shields.io/crates/v/cloudtoid-interprocess-ffi?label=C%20FFI)](https://crates.io/crates/cloudtoid-interprocess-ffi)
[![npm](https://img.shields.io/npm/v/@cloudtoid/interprocess?label=npm)](https://www.npmjs.com/package/@cloudtoid/interprocess)
[![Go](https://img.shields.io/github/v/tag/cloudtoid/interprocess?filter=src%2Fgo%2Fv*&label=Go)](https://pkg.go.dev/github.com/cloudtoid/interprocess/src/go/v3)
[![C SDK](https://img.shields.io/github/v/release/cloudtoid/interprocess?filter=native-v*&label=C%20SDK)](https://github.com/cloudtoid/interprocess/releases/latest)

[![Native core](https://github.com/cloudtoid/interprocess/actions/workflows/native.yml/badge.svg)](https://github.com/cloudtoid/interprocess/actions/workflows/native.yml)
[![Interoperability](https://github.com/cloudtoid/interprocess/actions/workflows/interop.yml/badge.svg)](https://github.com/cloudtoid/interprocess/actions/workflows/interop.yml)
[![License: MIT][LicenseBadge]][License]

**Fast, lightweight queues across processes and languages.** Cloudtoid Interprocess connects **Rust, C/C++, Python, Node.js, Go, and .NET** processes through a shared circular buffer on the same machine. Multiple publishers send bytes; competing subscribers receive them. No broker service or network hop is required.

- **Fast:** Measured Rust send + receive in **12.98 ns** for a 3-byte message on Apple M5 Max. See the [in-process benchmarks](#performance).
- **Low overhead:** Bounded shared memory, reusable receive buffers, and coalesced notifications. The measured .NET path with a reused buffer allocates **0 B per operation**.
- **Cross-language:** One documented v3 protocol, with tests across every publisher/subscriber language pair.
- **Concurrent:** Up to **2,048 publisher objects** per queue, with multiple subscribers sharing the work.
- **Cross-platform:** Windows, Linux (glibc), and macOS on supported little-endian 64-bit architectures.

Interprocess is used internally by Microsoft.

## Languages

.NET, Rust, Node.js, Go, and the C SDK are released and available to install. Python is available from source; publishing to PyPI is pending.

| Language | Package | Setup and API guide |
| --- | --- | --- |
| Rust | [`cloudtoid-interprocess`](https://crates.io/crates/cloudtoid-interprocess) (Cargo) | [Rust core](https://cloudtoid.com/docs/rust/) |
| C / C++ | [C SDK](https://github.com/cloudtoid/interprocess/releases/latest); [`cloudtoid-interprocess-ffi`](https://crates.io/crates/cloudtoid-interprocess-ffi) (Cargo) | [C ABI, headers, and shared library](https://cloudtoid.com/docs/c/) |
| Python | Source build (PyPI pending); import `cloudtoid_interprocess` | [Python 3.9+](https://cloudtoid.com/docs/python/) |
| Node.js | [`@cloudtoid/interprocess`](https://www.npmjs.com/package/@cloudtoid/interprocess) (npm) | [Node.js 18+, JavaScript and TypeScript](https://cloudtoid.com/docs/node/) |
| Go | [`github.com/cloudtoid/interprocess/src/go/v3`](https://pkg.go.dev/github.com/cloudtoid/interprocess/src/go/v3) | [Go 1.24+, cgo, and the C SDK](https://cloudtoid.com/docs/go/) |
| .NET | [`Cloudtoid.Interprocess`][NuGet] (NuGet) | [.NET 10+, C# and dependency injection](https://cloudtoid.com/docs/dotnet/) |

Rust supplies the native engine; C, Python, Node.js, and Go use that engine. .NET has its own managed implementation of the same protocol. Node's platform binaries are companion `@cloudtoid/interprocess-*` packages; applications use the main package.

## Quick start

Choose a language in the table above for installation commands, a working example, and its API reference. Start with [queue concepts](https://cloudtoid.com/docs/concepts/) when connecting separate processes or mixing languages. For a complete two-process example, follow the [Rust-to-Python shared-memory messaging tutorial](https://cloudtoid.com/docs/python-rust/).

Queues are transient: keep at least one publisher or subscriber connected throughout the handoff. Once all endpoints are gone, unread messages are lost and reopening the queue starts fresh.

## Faster with v3

The .NET v3 benchmarks show **12.0× faster round trips and 2.3× the concurrent throughput of v2** on the same native Mac. Version 3 coalesces notifications, avoiding repeated operating-system calls while readers are active.

| .NET version | 8-byte send + receive | 4 publishers / 4 subscribers |
| --- | ---: | ---: |
| Latest v1 (`1.0.175`) | — | — |
| Latest v2 (`2.1.204`) | 214.6 ns | 1.04 million messages/s |
| v3 | **17.82 ns** | **2.42 million messages/s** |

Measured with .NET 10. [Original results and comparison harness](https://github.com/cloudtoid/interprocess/tree/95c512672d580dd836ba2554cd1f78c0c3826f6c/docs/benchmarks/2026-09-13). See [benchmark details](#on-macos). The new language libraries start with protocol v3.

Upgrade existing v1/v2 applications together: drain the queue, stop all participants, and reopen a fresh queue using v3. All participants sharing a queue must use the same protocol.

## Performance

Send means enqueue; receive means dequeue. A send + receive operation includes both operations; a send-only measurement excludes receiving. All benchmarks keep publishers and subscribers connected throughout measurement, with queue creation and cleanup outside the timed work.

### On macOS

Measured on an **Apple M5 Max**, macOS 26.6.2, Release builds. Rust: September 14, 2026, Rust 1.98.1 ([raw samples](src/website/benchmarks/rust-macos.txt)). .NET: September 13, 2026, .NET 10.0.12 ([source `f990ba3`](https://github.com/cloudtoid/interprocess/commit/f990ba3)).

| Implementation | Workload | Mean (ns) | StdDev (ns) | Allocated |
| --- | --- | ---: | ---: | ---: |
| Rust | Send + receive, 3 bytes, reused buffer | 12.98 | 0.18 | — |
| Rust | Send + receive, 50 bytes, reused buffer | 16.05 | 0.32 | — |
| Rust | Send + receive, 1,024 bytes, reused buffer | 56.33 | 0.83 | — |
| .NET | Send, 3 bytes | 5.74 | 0.13 | 0 B |
| .NET | Send + receive, 3 bytes, reused buffer | 17.37 | 0.43 | 0 B |
| .NET | Send + receive, 3 bytes, new result array | 20.00 | 0.15 | 32 B |
| .NET | Send + receive, 50 bytes, reused buffer | 18.81 | 0.23 | 0 B |
| .NET | Send + receive, 50 bytes, ring-wrap workload | 21.35 | 0.08 | 0 B |
| .NET | Concurrent delivery, 8 bytes, 1 publisher / 1 subscriber | 113.40 | 0.95 | — |
| .NET | Concurrent delivery, 8 bytes, 1 publisher / 4 subscribers | 155.30 | 11.19 | — |

In-process microbenchmarks, not latency between applications. Rust uses one million operations per sample, four warmups, eight measured samples, a 1 MiB queue, and reused receive storage; allocations were not separately instrumented. The language harnesses differ, so these are not a controlled language comparison. Rust numbers exclude binding overhead for C, Python, Node.js, and Go. Concurrent rows show amortized time per message, including worker startup and completion; their allocations were not measured. Send-only drains outside the timed batch. [BenchmarkDotNet][BenchmarkOrg]: two launches, eight measured iterations, 20 warmups (200 for send-only; three for concurrent delivery).

[.NET benchmark source](src/dotnet/Interprocess.Benchmark/) · [Original reports](https://github.com/cloudtoid/interprocess/tree/95c512672d580dd836ba2554cd1f78c0c3826f6c/docs/benchmarks/2026-09-13). Run the Mac suites from the repository root:

```sh
cargo run --release --locked -p cloudtoid-interprocess --example benchmark
dotnet run --project src/dotnet/Interprocess.Benchmark -c Release -- --filter '*QueueBenchmark*' '*QueueExtendedBenchmark*' --warmupCount 20 --iterationCount 8 --launchCount 2 --iterationTime 250
dotnet run --project src/dotnet/Interprocess.Benchmark -c Release -- --filter '*EnqueueBenchmark*' --warmupCount 200 --iterationCount 8 --launchCount 2
dotnet run --project src/dotnet/Interprocess.Benchmark -c Release -- --filter '*SubscriberBenchmark*' --warmupCount 3 --iterationCount 8 --launchCount 2 --iterationTime 250
```

### On Windows

Measured September 13, 2026, on an **Apple M5 Max**, Windows 11 Pro 25H2 ARM64 VM (UTM, 4 vCPUs, 12 GiB RAM), .NET 10.0.12, Release build. V3 source: [`f990ba3`](https://github.com/cloudtoid/interprocess/commit/f990ba3).

| Workload | Mean (ns) | StdDev (ns) | Allocated |
| --- | ---: | ---: | ---: |
| Send, 3 bytes | 6.26 | 0.26 | 0 B |
| Send + receive, 3 bytes, reused buffer | 17.96 | 0.43 | 0 B |
| Send + receive, 3 bytes, new result array | 19.78 | 0.38 | 32 B |
| Send + receive, 50 bytes, reused buffer | 17.98 | 0.28 | 0 B |
| Send + receive, 50 bytes, ring-wrap workload | 21.81 | 0.30 | 0 B |
| Concurrent delivery, 8 bytes, 1 publisher / 1 subscriber | 101.30 | 1.68 | — |
| Concurrent delivery, 8 bytes, 1 publisher / 4 subscribers | 85.62 | 12.50 | — |

Same .NET workloads and allocation conventions as the Mac suite. Two launches and eight measured iterations; single-thread runs used one pinned vCPU and 20 warmups (200 for send-only), while concurrent runs used all four vCPUs and three warmups. [.NET benchmark source](src/dotnet/Interprocess.Benchmark/) · [Original reports](https://github.com/cloudtoid/interprocess/tree/95c512672d580dd836ba2554cd1f78c0c3826f6c/docs/benchmarks/2026-09-13).

### On Linux

Measured September 13, 2026, on an **Apple M5 Max**, Ubuntu 24.04 ARM64 VM (Lima/QEMU, 4 vCPUs, 8 GiB RAM), Linux 6.12.94 with 16 KiB pages, .NET 10.0.12, Release build. V3 source: [`f990ba3`](https://github.com/cloudtoid/interprocess/commit/f990ba3).

| Workload | Mean (ns) | StdDev (ns) | Allocated |
| --- | ---: | ---: | ---: |
| Send, 3 bytes | 6.00 | 0.07 | 0 B |
| Send + receive, 3 bytes, reused buffer | 16.68 | 0.18 | 0 B |
| Send + receive, 3 bytes, new result array | 19.78 | 0.20 | 32 B |
| Send + receive, 50 bytes, reused buffer | 17.64 | 0.21 | 0 B |
| Send + receive, 50 bytes, ring-wrap workload | 20.82 | 0.11 | 0 B |
| Concurrent delivery, 8 bytes, 1 publisher / 1 subscriber | 112.90 | 1.10 | — |
| Concurrent delivery, 8 bytes, 1 publisher / 4 subscribers | 158.60 | 13.64 | — |

Same .NET workloads and allocation conventions as the Mac suite. Two launches and eight measured iterations; single-thread runs used one pinned vCPU and 20 warmups (200 for send-only), while concurrent runs used all four vCPUs and three warmups. [.NET benchmark source](src/dotnet/Interprocess.Benchmark/) · [Original reports](https://github.com/cloudtoid/interprocess/tree/95c512672d580dd836ba2554cd1f78c0c3826f6c/docs/benchmarks/2026-09-13).

[Protocol v3](https://cloudtoid.com/docs/protocol/) documents the complete shared-memory format and synchronization rules. [Interoperability tests](tests/interop/README.md) exercise every publisher/subscriber language pair and mixed-language concurrent delivery across participant crashes.

## How to Contribute

- Create a branch from `main`.
- Ensure that all tests pass on Windows, Linux, and macOS.
- Keep the code coverage number above 80% by adding new tests or modifying the existing tests.
- Send a pull request.

## Author

[**Pedram Rezaei**][PedramLinkedIn] is a software architect at Microsoft with years of experience building highly scalable and reliable cloud-native applications for Microsoft.

[Cloudtoid]:https://github.com/cloudtoid
[License]:https://github.com/cloudtoid/interprocess/blob/main/LICENSE
[LicenseBadge]:https://img.shields.io/badge/License-MIT-blue.svg
[NuGet]:https://www.nuget.org/packages/Cloudtoid.Interprocess/
[BenchmarkOrg]:https://benchmarkdotnet.org/
[PedramLinkedIn]:https://www.linkedin.com/in/pedramrezaei/
