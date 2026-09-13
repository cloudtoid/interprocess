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

## NuGet Package

The NuGet package for this library is published [here][NuGet].

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
subscriber.TryDequeue(messageBuffer, cancellationToken, out var message);
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
subscriber.TryDequeue(messageBuffer, cancellationToken, out var message);
```

### Queue lifetime

Dispose each publisher and subscriber when finished. The queue, including unread messages, stays alive
while any participant remains. After the last participant is disposed, the backing memory and named
semaphore are removed. If the last process is forcibly terminated, the next connection resets the
abandoned resources and starts with an empty queue.

Publisher disposal stops new enqueue calls and waits for in-flight calls to finish before releasing
shared memory and the semaphore. `TryEnqueue` throws `ObjectDisposedException` once admission closes.

All participants must use the same queue name, storage path, and capacity. On Unix, keep the storage
directory in place while queues are active; creation and cleanup use advisory file locks on that directory
and the backing files. Stop all participants before upgrading to this lifecycle implementation.

## Sample

To see a sample implementation of a publisher and a subscriber process, try out the following two projects. You can run them side by side and see them in action:

- [Publisher](src/Sample/Publisher/)
- [Subscriber](src/Sample/Subscriber/)

Please note that you can start multiple publishers and subscribers sending and receiving messages to and from the same message queue.

## Performance

A lot has gone into optimizing the implementation of this library. For instance, it is mostly heap-memory allocation free, reducing the need for garbage collection induced pauses.

**Latest native macOS measurement**: a three-byte enqueue/dequeue round trip with a reused buffer averaged **210.0 ns** on an Apple M5 Max. Only the macOS results below were refreshed on September 12, 2026; the Windows and Linux sections retain their historical measurements.

**Details**: To benchmark the performance and memory usage, we use [BenchmarkDotNet][BenchmarkOrg] and perform the following runs:

|                                          Method |   Description |
|------------------------------------------------ |-------------- |
|                     Message enqueue and dequeue | Benchmarks the performance of sending a message to a client and receiving that message. It is inclusive of the duration to enqueue and dequeue a message. |
| Message enqueue and dequeue - no message buffer | Benchmarks the performance of sending a message to a client and receiving that message. It is inclusive of the duration to enqueue and dequeue a message and memory allocation for the received message. |

You can replicate the results by running the following command:

```sh
dotnet run --project src/Interprocess.Benchmark -c Release -- --filter '*QueueBenchmark*'
```

To compare throughput with one subscriber versus four concurrent subscribers:

```sh
dotnet run --project src/Interprocess.Benchmark -c Release -- --filter '*SubscriberBenchmark*' --iterationCount 8
```

---

### On Windows

Host:

```text
BenchmarkDotNet=v0.13.1, OS=Windows 10.0.22000
Intel Core i9-10900X CPU 3.70GHz, 1 CPU, 20 logical and 10 physical cores
.NET SDK=6.0.201
  [Host]   : .NET 6.0.3 (6.0.322.12309), X64 RyuJIT
  .NET 6.0 : .NET 6.0.3 (6.0.322.12309), X64 RyuJIT
```

Results:

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                     Message enqueue and dequeue |    `305.6`|      `5.96`|       `6.62`|       `-` |
| Message enqueue and dequeue - no message buffer |    `311.5`|      `5.90`|       `9.85`|    `32 B` |

---

### On macOS

Measured **September 12, 2026**, running directly on the Mac:

```text
BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M5 Max, 1 CPU, 18 logical and 18 physical cores
.NET SDK 10.0.401
.NET runtime 10.0.12, Arm64 RyuJIT
Release build; 3 warm-up iterations; 8 measured iterations; 1 launch
```

All seven cases completed. Times are means in nanoseconds, normalized per operation. For enqueue/dequeue rows, an operation is one complete round trip. Concurrent-delivery rows report amortized time per delivered message.

| Workload | Mean (ns) | StdDev (ns) | Allocated per operation |
| --- | ---: | ---: | ---: |
| Enqueue, 3 bytes | 182.3 | 5.01 | 0 B |
| Enqueue + dequeue, 3 bytes, reused buffer | 210.0 | 0.46 | 0 B |
| Enqueue + dequeue, 3 bytes, new result array | 214.9 | 0.81 | 32 B |
| Enqueue + dequeue, 50 bytes, reused buffer | 214.6 | 1.33 | 0 B |
| Enqueue + dequeue, 50 bytes, ring-wrap workload | 223.8 | 1.08 | 0 B |
| Concurrent delivery, 8 bytes, 1 subscriber | 246.5 | 2.20 | Not measured |
| Concurrent delivery, 8 bytes, 4 subscribers | 344.7 | 1.81 | Not measured |

The enqueue case batches 320,000 messages and drains the queue outside the timed body. The ring-wrap case uses a 120-byte queue so padded 64-byte records repeatedly cross the end of the buffer; two round trips per invocation are normalized to one. Concurrent delivery uses one publisher and dedicated subscriber threads to transfer batches of 65,536 messages, including worker startup and completion in the timing.

These are in-process microbenchmarks, not end-to-end latency between separate applications. The concurrent cases measure throughput under contention, not individual message latency; their allocations were not measured. See the [complete native Mac reports and methodology](docs/benchmarks/2026-09-12/README.md) for source revision, errors, and reproduction details.

Run all cases from the repository root:

```sh
dotnet run --project src/Interprocess.Benchmark -c Release -- --filter '*' --warmupCount 3 --iterationCount 8 --artifacts BenchmarkDotNet.Artifacts
```

---

### On Ubuntu (through [WSL][WslDoc])

Host:

```text
BenchmarkDotNet=v0.13.2, OS=ubuntu 20.04
Intel Core i9-10900X CPU 3.70GHz, 1 CPU, 20 logical and 10 physical cores
.NET SDK=6.0.403
  [Host]   : .NET 6.0.11 (6.0.1122.52304), X64 RyuJIT AVX2
  .NET 6.0 : .NET 6.0.11 (6.0.1122.52304), X64 RyuJIT AVX2
```

Results:

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                     Message enqueue and dequeue |    `169.9`|      `3.08`|       `4.01`|        `-`|
| Message enqueue and dequeue - no message buffer |    `179.4`|      `1.91`|       `1.60`|     `32 B`|

## Implementation Notes

This library relies on [Named Semaphores][NamedSemaphoresDoc] To signal the existence of a new message to all message subscribers and to do it across process boundaries. Named semaphores are synchronization constructs accessible across processes.

.NET currently does not support named semaphores on Unix-based OSs (Linux, macOS, etc.). Instead we are using P/Invoke and relying on operating system's POSIX semaphore implementation. ([Linux](src/Interprocess/Semaphore/Linux/Interop.cs) and [macOS](src/Interprocess/Semaphore/macOS/Interop.cs) implementations).

This implementation will be replaced with [`System.Threading.Semaphore`][SemaphoreDoc] once .NET adds support for named semaphores on all platforms.

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
[WslDoc]:https://learn.microsoft.com/windows/wsl/about
[BenchmarkOrg]:https://benchmarkdotnet.org/
[NamedSemaphoresDoc]:https://docs.microsoft.com/dotnet/api/system.threading.semaphore#remarks
[SemaphoreDoc]:https://docs.microsoft.com/dotnet/api/system.threading.semaphore
[PedramLinkedIn]:https://www.linkedin.com/in/pedramrezaei/
