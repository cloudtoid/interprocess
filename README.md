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
    bytesCapacity: 1024 * 1024);

using var publisher = factory.CreatePublisher(options);
publisher.TryEnqueue(message);
```

Creating a message queue subscriber:

```csharp
options = new QueueOptions(
    queueName: "my-queue",
    bytesCapacity: 1024 * 1024);

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
    bytesCapacity: 1024 * 1024);

using var publisher = factory.CreatePublisher(options);
publisher.TryEnqueue(message);
```

Creating a message queue subscriber using an instance of `IQueueFactory` retrieved from the DI container:

```csharp
var options = new QueueOptions(
    queueName: "my-queue",
    bytesCapacity: 1024 * 1024);

using var subscriber = factory.CreateSubscriber(options);
subscriber.TryDequeue(messageBuffer, cancellationToken, out var message);
```

## Sample

To see a sample implementation of a publisher and a subscriber process, try out the following two projects. You can run them side by side and see them in action:

- [Publisher](src/Sample/Publisher/)
- [Subscriber](src/Sample/Subscriber/)

Please note that you can start multiple publishers and subscribers sending and receiving messages to and from the same message queue.

## Performance

A lot has gone into optimizing the implementation of this library. For instance, it is mostly heap-memory allocation free, reducing the need for garbage collection induced pauses.

**Summary**: A full enqueue followed by a dequeue takes `~250 ns` on Linux, `~650 ns` on macOS, and `~300 ns` on Windows.

**Details**: To benchmark the performance and memory usage, we use [BenchmarkDotNet][BenchmarkOrg] and perform the following runs:

|                                          Method |   Description |
|------------------------------------------------ |-------------- |
|                     Message enqueue and dequeue | Benchmarks the performance of sending a message to a client and receiving that message. It is inclusive of the duration to enqueue and dequeue a message. |
| Message enqueue and dequeue - no message buffer | Benchmarks the performance of sending a message to a client and receiving that message. It is inclusive of the duration to enqueue and dequeue a message and memory allocation for the received message. |

You can replicate the results by running the following command:

```sh
dotnet run Interprocess.Benchmark.csproj -c Release
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

Host:

```text
BenchmarkDotNet v0.14.0, macOS Sequoia 15.2 (24C101) [Darwin 24.2.0]
Apple M3 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 9.0.101
  [Host]   : .NET 9.0.0 (9.0.24.52809), Arm64 RyuJIT AdvSIMD
  .NET 9.0 : .NET 9.0.0 (9.0.24.52809), Arm64 RyuJIT AdvSIMD
```

|                                            Method | Mean (ns) | Error (ns) | StdDev | Gen0     | Allocated |
|-------------------------------------------------- |----------:|-----------:|-------:|---------:|----------:|
|                     'Message enqueue and dequeue' |   `249.2` |     `0.74` | `0.62` |      `-` |       `-` |
| 'Message enqueue and dequeue - no message buffer' |   `252.1` |     `4.10` | `3.83` | `0.0038` |    `32 B` |

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
[DotNetPlatformBadge]:https://img.shields.io/badge/.net-%3E%209.0-blue
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
