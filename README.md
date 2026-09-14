[<img src="https://raw.githubusercontent.com/cloudtoid/assets/master/logos/cloudtoid-blue.svg" width="100px">][Cloudtoid]

# Interprocess

[![Publish Workflow][WorkflowBadgePublish]][PublishWorkflow]
[![Latest NuGet][NuGetBadge]][NuGet]
[![License: MIT][LicenseBadge]][License]
![.NET Platform][DotNetPlatformBadge]

**Cloudtoid Interprocess** is a cross-platform shared memory queue for fast communication between processes ([Interprocess Communication or IPC][IPCWiki]). It uses a shared memory-mapped file for extremely fast and efficient communication between processes and it is used internally by Microsoft.

- [**Fast**](#performance): It is *extremely* fast.
- **Cross-platform**: It supports Windows, and Unix-based operating systems such as Linux, [macOS][macOSWiki], and [FreeBSD][FreeBSDOrg].
- [**API**](#usage): Provides a simple and intuitive API to enqueue/send and dequeue/receive messages.
- **Multiple publishers and subscribers**: It supports multiple publishers and subscribers to a shared queue.
- [**Efficient**](#performance): Sending and receiving messages is almost heap memory allocation free reducing garbage collections.
- [**Developer**](#author): Developed by a guy at Microsoft.

## Faster with v3 alpha

**11.7× faster round trips and 3.2× the concurrent throughput of v2** in our native Mac benchmarks. Version 3 coalesces notifications, avoiding repeated operating-system calls while readers are active.

| Version | 8-byte enqueue + dequeue | 4 publishers / 4 subscribers |
| --- | ---: | ---: |
| Latest v1 (`1.0.175`) | — | — |
| Latest v2 (`2.1.204`) | 208.3 ns | 1.02 million messages/s |
| v3 alpha | **17.77 ns** | **3.26 million messages/s** |

Measured on the same Mac with .NET 10. See [benchmark details](#on-macos). Windows ARM64 VM benchmarks also measured **49× faster round trips and 9.9× the throughput of v2**; see the [Windows results](#on-windows).

**Upgrade to v3 alpha and try it with your workload:**

```sh
dotnet add package Cloudtoid.Interprocess --prerelease
```

Alpha APIs and the shared-memory protocol may change. Drain the queue, stop all participants, and upgrade them together using a fresh queue.

## NuGet Package

The NuGet package for this library is published [here][NuGet]. Version 3 packages use `3.0.0-alpha.<build number>`
and require opting into prerelease versions.

> Note: To improve performance, this library only supports 64-bit CLR with 64-bit processor architectures. Attempting to use this library on 32-bit processors, 32-bit operating systems, or on [WOW64][Wow64Wiki] may throw a `NotSupportedException`.

## Usage

This library is optimized for .NET dependency injection but can also be used without DI.

### Usage without DI

Creating a message queue factory:

```csharp
var factory = new QueueFactory();
```

Creating a message queue publisher:

```csharp
var options = new QueueOptions(
    queueName: "my-queue",
    capacity: 1024 * 1024);

using var publisher = factory.CreatePublisher(options);
publisher.TryEnqueue(message);
```

Creating a message queue subscriber:

```csharp
options = new QueueOptions(
    queueName: "my-queue",
    capacity: 1024 * 1024);

using var subscriber = factory.CreateSubscriber(options);
subscriber.TryDequeue(messageBuffer, out var message);
```

### Usage with DI

Adding the queue factory to the DI container:

```csharp
services
    .AddInterprocessQueue() // adding the queue related components
    .AddLogging(); // optionally, we can enable logging
```

Creating a message queue publisher using an instance of `IQueueFactory` retrieved from the DI container:

```csharp
var options = new QueueOptions(
    queueName: "my-queue",
    capacity: 1024 * 1024);

using var publisher = factory.CreatePublisher(options);
publisher.TryEnqueue(message);
```

Creating a message queue subscriber using an instance of `IQueueFactory` retrieved from the DI container:

```csharp
var options = new QueueOptions(
    queueName: "my-queue",
    capacity: 1024 * 1024);

using var subscriber = factory.CreateSubscriber(options);
subscriber.TryDequeue(messageBuffer, out var message);
```

## Sample

To see a sample implementation of a publisher and a subscriber process, try out the following two projects. You can run them side by side and see them in action:

- [Publisher](src/Sample/Publisher/)
- [Subscriber](src/Sample/Subscriber/)

Please note that you can start multiple publishers and subscribers sending and receiving messages to and from the same message queue.

## Performance

### On macOS

Measured September 13, 2026, on an **Apple M5 Max**, macOS 26.6.2, .NET 10.0.12, Release build. V3 source: [`9fc6b15`](https://github.com/cloudtoid/interprocess/commit/9fc6b15).

| Workload | Mean (ns) | StdDev (ns) | Allocated |
| --- | ---: | ---: | ---: |
| Enqueue, 3 bytes | 4.84 | 0.07 | 0 B |
| Enqueue + dequeue, 3 bytes, reused buffer | 17.27 | 0.15 | 0 B |
| Enqueue + dequeue, 3 bytes, new result array | 19.85 | 0.23 | 32 B |
| Enqueue + dequeue, 50 bytes, reused buffer | 18.01 | 0.21 | 0 B |
| Enqueue + dequeue, 50 bytes, ring-wrap workload | 21.61 | 0.21 | 0 B |
| Concurrent delivery, 8 bytes, 1 publisher / 1 subscriber | 104.90 | 1.17 | — |
| Concurrent delivery, 8 bytes, 1 publisher / 4 subscribers | 139.70 | 6.43 | — |

In-process microbenchmarks, not latency between applications. Concurrent rows show amortized time per message, including worker startup and completion; their allocations were not measured. Enqueue drains outside the timed batch. [BenchmarkDotNet][BenchmarkOrg]: two launches, eight measured iterations, three warmups (200 for enqueue-only).

[Benchmark source and reports](docs/benchmarks/2026-09-13/). Run the Mac suite from the repository root:

```sh
dotnet run --project src/Interprocess.Benchmark -c Release -- --filter '*' --warmupCount 3 --iterationCount 8 --launchCount 2 --iterationTime 250
dotnet run --project src/Interprocess.Benchmark -c Release -- --filter '*EnqueueBenchmark*' --warmupCount 200 --iterationCount 8 --launchCount 2
```

### On Windows

Measured September 13, 2026, on an **Apple M5 Max**, Windows 11 Pro 25H2 ARM64 VM (UTM, 4 vCPUs, 12 GiB RAM), .NET 10.0.12, Release build. V3 source: [`9fc6b15`](https://github.com/cloudtoid/interprocess/commit/9fc6b15).

| Workload | Mean (ns) | StdDev (ns) | Allocated |
| --- | ---: | ---: | ---: |
| Enqueue, 3 bytes | 4.92 | 0.08 | 0 B |
| Enqueue + dequeue, 3 bytes, reused buffer | 18.47 | 0.43 | 0 B |
| Enqueue + dequeue, 3 bytes, new result array | 19.61 | 0.42 | 32 B |
| Enqueue + dequeue, 50 bytes, reused buffer | 18.10 | 0.49 | 0 B |
| Enqueue + dequeue, 50 bytes, ring-wrap workload | 22.16 | 0.24 | 0 B |
| Concurrent delivery, 8 bytes, 1 publisher / 1 subscriber | 94.91 | 0.73 | — |
| Concurrent delivery, 8 bytes, 1 publisher / 4 subscribers | 95.08 | 14.96 | — |

Same in-process workloads and allocation conventions as the Mac suite. Two launches and eight measured iterations; single-thread runs used one pinned vCPU and 20 warmups (200 for enqueue-only), while concurrent runs used all four vCPUs and three warmups. [Benchmark source and reports](docs/benchmarks/2026-09-13/).

### On Linux

Measured September 13, 2026, on an **Apple M5 Max**, Ubuntu 24.04 ARM64 VM (Lima/QEMU, 4 vCPUs, 8 GiB RAM), Linux 6.12.94 with 16 KiB pages, .NET 10.0.12, Release build. V3 source: [`9fc6b15`](https://github.com/cloudtoid/interprocess/commit/9fc6b15).

| Workload | Mean (ns) | StdDev (ns) | Allocated |
| --- | ---: | ---: | ---: |
| Enqueue, 3 bytes | 5.05 | 0.07 | 0 B |
| Enqueue + dequeue, 3 bytes, reused buffer | 17.97 | 0.48 | 0 B |
| Enqueue + dequeue, 3 bytes, new result array | 21.15 | 0.27 | 32 B |
| Enqueue + dequeue, 50 bytes, reused buffer | 18.20 | 0.16 | 0 B |
| Enqueue + dequeue, 50 bytes, ring-wrap workload | 21.65 | 0.46 | 0 B |
| Concurrent delivery, 8 bytes, 1 publisher / 1 subscriber | 107.70 | 1.73 | — |
| Concurrent delivery, 8 bytes, 1 publisher / 4 subscribers | 161.00 | 13.33 | — |

Same in-process workloads and allocation conventions as the Mac suite. Two launches and eight measured iterations; single-thread runs used one pinned vCPU and 20 warmups (200 for enqueue-only), while concurrent runs used all four vCPUs and three warmups. [Benchmark source and reports](docs/benchmarks/2026-09-13/).

## Implementation Notes

Messages travel through a shared, circular memory-mapped buffer. Coalesced notifications reduce operating-system calls while keeping blocked subscribers responsive. Cross-process wakeups use named semaphores, with POSIX implementations on [Linux](src/Interprocess/Semaphore/Linux/Interop.cs) and [macOS](src/Interprocess/Semaphore/MacOS/Interop.cs).

## How to Contribute

- Create a branch from `main`.
- Ensure that all tests pass on Windows, Linux, and macOS.
- Keep the code coverage number above 80% by adding new tests or modifying the existing tests.
- Send a pull request.

## Author

[**Pedram Rezaei**][PedramLinkedIn] is a software architect at Microsoft with years of experience building highly scalable and reliable cloud-native applications for Microsoft.

## What is next

Here are a couple of items that we are working on.

- Create a marketing/documentation website
- Once .NET supports named semaphores on Linux, then start using them.

[Cloudtoid]:https://github.com/cloudtoid
[License]:https://github.com/cloudtoid/interprocess/blob/main/LICENSE
[LicenseBadge]:https://img.shields.io/badge/License-MIT-blue.svg
[WorkflowBadgePublish]:https://github.com/cloudtoid/interprocess/workflows/publish/badge.svg
[PublishWorkflow]:https://github.com/cloudtoid/interprocess/actions/workflows/publish.yml
[NuGetBadge]:https://img.shields.io/nuget/vpre/Cloudtoid.Interprocess
[DotNetPlatformBadge]:https://img.shields.io/badge/.net-%3E%3D%2010.0-blue
[NuGet]:https://www.nuget.org/packages/Cloudtoid.Interprocess/
[IPCWiki]:https://en.wikipedia.org/wiki/Inter-process_communication
[macOSWiki]:https://en.wikipedia.org/wiki/macOS
[FreeBSDOrg]:https://www.freebsd.org/
[Wow64Wiki]:https://en.wikipedia.org/wiki/WoW64
[BenchmarkOrg]:https://benchmarkdotnet.org/
[PedramLinkedIn]:https://www.linkedin.com/in/pedramrezaei/
