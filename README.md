[<img src="https://raw.githubusercontent.com/cloudtoid/assets/master/logos/cloudtoid-blue.svg" width="100px">][Cloudtoid]

# Interprocess

[![][WorkflowBadgePublish]][PublishWorkflow] [![License: MIT][LicenseBadge]][License] [![][NuGetBadge]][NuGet] ![][DotNet31Badge] ![][DotNet50Badge]

**Cloudtoid Interprocess** is a cross-platform shared memory queue for fast communication between processes ([Interprocess Communication or IPC][IPCWiki]). It uses a shared memory-mapped file for extremely fast and efficient communication between processes and it is used internally by Microsoft.

- [**Fast**](#performance): It is *extremely* fast.
- **Cross-platform**: It supports Windows, and Unix-based operating systems such as Linux, [MacOS][MacOSWiki], and [FreeBSD][FreeBSDOrg].
- [**API**](#Usage): Provides a simple and intuitive API to enqueue/send and dequeue/receive messages.
- **Multiple publishers and subscribers**: It supports multiple publishers and subscribers to a shared queue.
- [**Efficient**](#performance): Sending and receiving messages is almost heap memory allocation free reducing garbage collections.
- [**Developer**](#Author): Developed by a guy at Microsoft.

## NuGet Package

The NuGet package for this library is published [here][NuGet].

> Note: To improve performance, this library only supports 64-bit CLR with 64-bit processor architectures. Attempting to use this library on 32-bit processors, 32-bit operating systems, or on [WOW64][Wow64Wiki] may throw a `NotSupportedException`.

## Usage

This library supports .NET Core 3.1+ and .NET 5+. It is optimized for .NET dependency injection but can also be used without DI.

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

**Summary**: A full enqueue followed by a dequeue takes `~700 ns` on Windows, `~250 ns` on Linux, and `~650 ns` on MacOS.

**Details**: To benchmark the performance and memory usage, we use [BenchmarkDotNet][BenchmarkOrg] and perform the following runs:

|                                          Method |   Description |
|------------------------------------------------ |-------------- |
|                                 Message enqueue | Benchmarks the performance of enqueuing a message. |
|                     Message enqueue and dequeue | Benchmarks the performance of sending a message to a client and receiving that message. It is inclusive of the duration to enqueue and dequeue a message. |
| Message enqueue and dequeue - no message buffer | Benchmarks the performance of sending a message to a client and receiving that message. It is inclusive of the duration to enqueue and dequeue a message and memory allocation for the received message. |

You can replicate the results by running the following command:

```posh
dotnet run Interprocess.Benchmark.csproj -c Release
```

You can also be explicit about the .NET SDK and Runtime(s) versions:

```posh
dotnet run Interprocess.Benchmark.csproj -c Release -f net5.0 --runtimes net5.0 netcoreapp3.1
```

---

### On Windows

Host:

```text
BenchmarkDotNet=v0.13.1, OS=Windows 10.0.19043.1237 (21H1/May2021Update)
Intel Xeon CPU E5-1620 v3 3.50GHz, 1 CPU, 8 logical and 4 physical cores
.NET SDK=5.0.401
  [Host]        : .NET 5.0.10 (5.0.1021.41214), X64 RyuJIT
  .NET 5.0      : .NET 5.0.10 (5.0.1021.41214), X64 RyuJIT
```

Results:

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                                 Message enqueue |    `620.1`|     `10.86`|      `10.18`|       `-` |
|                     Message enqueue and dequeue |    `712.9`|     `13.57`|      `14.52`|       `-` |
| Message enqueue and dequeue - no message buffer |    `744.7`|     `14.81`|      `31.88`|    `32 B` |

---

### On MacOS

Host:

```text
BenchmarkDotNet=v0.13.1, OS=macOS Big Sur 11.6 (20G165) [Darwin 20.6.0]
Intel Core i5-8279U CPU 2.40GHz (Coffee Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK=5.0.401
  [Host]        : .NET 5.0.10 (5.0.1021.41214), X64 RyuJIT
  .NET 5.0      : .NET 5.0.10 (5.0.1021.41214), X64 RyuJIT
```

Results:

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                                 Message enqueue |   `487.50`|      `4.75`|       `3.96`|        `-`|
|                     Message enqueue and dequeue |   `666.10`|     `10.91`|      `10.20`|        `-`|
| Message enqueue and dequeue - no message buffer |   `689.33`|     `13.38`|      `15.41`|     `32 B`|

---

### On Ubuntu (through [WSL][WslDoc])

Host:

```text
BenchmarkDotNet=v0.13.1, OS=ubuntu 20.04
Intel Xeon CPU E5-1620 v3 3.50GHz, 1 CPU, 8 logical and 4 physical cores
.NET SDK=5.0.401
  [Host]        : .NET 5.0.10 (5.0.1021.41214), X64 RyuJIT
  .NET 5.0      : .NET 5.0.10 (5.0.1021.41214), X64 RyuJIT
```

Results:

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                                 Message enqueue |     `81.3`|      `5.98`|      `1.659`|        `-`|
|                     Message enqueue and dequeue |    `247.9`|      `4.96`|      `12.16`|        `-`|
| Message enqueue and dequeue - no message buffer |    `265.8`|      `5.33`|      `15.03`|     `32 B`|

## Implementation Notes

This library relies on [Named Semaphores][NamedSemaphoresDoc] To signal the existence of a new message to all message subscribers and to do it across process boundaries. Named semaphores are synchronization constructs accessible across processes.

.NET Core 3.1 and .NET 5 do not support named semaphores on Unix-based OSs (Linux, macOS, etc.). Instead we are using P/Invoke and relying on operating system's POSIX semaphore implementation. ([Linux](src/Interprocess/Semaphore/Linux/Interop.cs) and [MacOS](src/Interprocess/Semaphore/MacOS/Interop.cs) implementations).

This implementation will be replaced with [`System.Threading.Semaphore`][SemaphoreDoc] once .NET adds support for named semaphores on all platforms.

## How to Contribute

- Create a branch from `main`.
- Ensure that all tests pass on Windows, Linux, and MacOS.
- Keep the code coverage number above 80% by adding new tests or modifying the existing tests.
- Send a pull request.

## Author

[**Pedram Rezaei**][PedramLinkedIn] is a software architect at Microsoft with years of experience building highly scalable and reliable cloud-native applications for Microsoft.

## What is next

Here are a couple of items that we are working on.

- Create a documentation website

[Cloudtoid]:https://github.com/cloudtoid
[License]:https://github.com/cloudtoid/interprocess/blob/main/LICENSE
[LicenseBadge]:https://img.shields.io/badge/License-MIT-blue.svg
[WorkflowBadgePublish]:https://github.com/cloudtoid/interprocess/workflows/publish/badge.svg
[PublishWorkflow]:https://github.com/cloudtoid/interprocess/actions/workflows/publish.yml
[NuGetBadge]:https://img.shields.io/nuget/vpre/Cloudtoid.Interprocess
[DotNet31Badge]:https://img.shields.io/badge/.net%20core-%3E%203.1-blue
[DotNet50Badge]:https://img.shields.io/badge/.net-%3E%205.0-blue
[NuGet]:https://www.nuget.org/packages/Cloudtoid.Interprocess/
[IPCWiki]:https://en.wikipedia.org/wiki/Inter-process_communication
[MacOSWiki]:https://en.wikipedia.org/wiki/MacOS
[FreeBSDOrg]:https://www.freebsd.org/
[Wow64Wiki]:https://en.wikipedia.org/wiki/WoW64
[WslDoc]:https://docs.microsoft.com/en-us/windows/wsl/about
[BenchmarkOrg]:https://benchmarkdotnet.org/
[NamedSemaphoresDoc]:https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphore#remarks
[SemaphoreDoc]:https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphore
[PedramLinkedIn]:https://www.linkedin.com/in/pedramrezaei/