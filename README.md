<a href="https://github.com/cloudtoid"><img src="https://raw.githubusercontent.com/cloudtoid/assets/master/logos/cloudtoid-black-red.png" width="100"></a>

# Interprocess

![](https://github.com/cloudtoid/interprocess/workflows/publish/badge.svg) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/cloudtoid/url-patterns/blob/master/LICENSE) ![https://www.nuget.org/packages/Cloudtoid.Interprocess/](https://img.shields.io/nuget/vpre/Cloudtoid.Interprocess) ![](https://img.shields.io/badge/.net%20core-%3E%203.1.0-blue)

**Cloudtoid Interprocess** is a cross-platform shared memory queue for fast communication between processes ([Interprocess Communication or IPC](https://en.wikipedia.org/wiki/Inter-process_communication)). It uses a shared memory-mapped file for extremely fast and efficient communication between processes.

- [**Fast**](#performance): It is *extremely* fast.
- **Cross-platform**: It supports Windows, and Unix-based operating systems such as Linux, [OSX](https://en.wikipedia.org/wiki/MacOS), and [FreeBSD](https://www.freebsd.org/).
- [**API**](#Usage): Provides a simple and intuitive API to enqueue/send and dequeue/receive messages.
- **Multiple publishers and subscribers**: It supports multiple publishers and subscribers to a shared queue.
- [**Efficient**](#performance): Sending and receiving messages is an almost heap memory allocation free reducing garbage collections.
- [**Developer**](#Author): Developed by folks at Microsoft.

## NuGet Package

The NuGet package for this library is published [here](https://www.nuget.org/packages/Cloudtoid.Interprocess/).

> Note: To improve performance, this library only supports 64 bit CLR with 64-bit processor architectures. Attempting to use this library on 32-bit processors, 32-bit operating systems, or on [WOW64](https://en.wikipedia.org/wiki/WoW64) may throw a `NotSupportedException`.

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
    bytesCapacity: 1024 * 1024,
    createOrOverride: true);

using var publisher = factory.CreatePublisher(options);
publisher.TryEnqueue(message);
```

Creating a message queue subscriber:

```csharp
options = new QueueOptions(
    queueName: "my-queue",
    bytesCapacity: 1024 * 1024);

using var subscriber = factory.CreateSubscriber(options);
await subscriber.TryDequeueAsync(messageBuffer, cancellationToken, out var message);
```

### Usage with DI

Adding the queue factory to the DI container:

```csharp
services
    .AddInterprocessQueue() // adding the queue related components
    .AddLogging(); // optionally, we can enable logging
```

Creating a message queue publisher using an instance `IQueueFactory` retrieved from the DI container:

```csharp
var options = new QueueOptions(
    queueName: "my-queue",
    bytesCapacity: 1024 * 1024,
    createOrOverride: true);

using var publisher = factory.CreatePublisher(options);
publisher.TryEnqueue(message);
```

Creating a message queue subscriber using an instance `IQueueFactory` retrieved from the DI container:

```csharp
var options = new QueueOptions(
    queueName: "my-queue",
    bytesCapacity: 1024 * 1024);

using var subscriber = factory.CreateSubscriber(options);
await subscriber.TryDequeueAsync(messageBuffer, cancellationToken, out var message);
```

## Sample

To see a sample implementation of a publisher and a subscriber process, try out the following two projects. You can run them side by side and see them in action:

- [Publisher](src/Sample/Publisher/)
- [Subscriber](src/Sample/Subscriber/)

Please note that you can start multiple publishers and subscribers sending and receiving messages to and from the same message queue.

## Performance

A lot has gone into optimizing the implementation of this library. For instance, it is mostly heap-memory allocation free, reducing the need for garbage collection induced pauses.

**Summary**: In average, enqueuing a message is about `~10 ns` and a full enqueue followed by a dequeue takes roughly `~500 ns` on Windows and `~1.5 us` on Unix-based operating systems.

**Details**: To benchmark the performance and memory usage, we use [BenchmarkDotNet](https://benchmarkdotnet.org/) and perform the following runs:

|                                          Method |   Description |
|------------------------------------------------ |-------------- |
|                                 Message enqueue | Benchmarks the performance of enqueuing a message. |
|                     Message enqueue and dequeue | Benchmarks the performance of sending a message to a client and receiving that message. It is inclusive of the duration to enqueue and dequeue a message. |
| Message enqueue and dequeue - no message buffer | Benchmarks the performance of sending a message to a client and receiving that message. It is inclusive of the duration to enqueue and dequeue a message and memory allocation for the received message. |

You can replicate the results by running the following command:

```posh
dotnet run Interprocess.Benchmark.csproj --configuration Release
```

You can also be explicit about the .NET SDK and Runtime(s) versions:

```posh
dotnet run Interprocess.Benchmark.csproj --configuration Release --framework net5.0 --runtimes net5.0 netcoreapp3.1
```

---

### On Windows

#### Host

```ini
OS=Windows 10.0.19041.450
Intel Xeon CPU E5-1620 v3 3.50GHz, 1 CPU, 8 logical and 4 physical cores
.NET Core SDK=3.1.401
  [Host]        : .NET Core 3.1.7, X64 RyuJIT
  .NET Core 3.1 : .NET Core 3.1.7, X64 RyuJIT
```

#### Results

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                                 Message enqueue |    `6.138`|    `0.1641`|     `0.3315`|       `-` |
|                     Message enqueue and dequeue |  `584.651`|   `11.5850`|    `23.6650`|       `-` |
| Message enqueue and dequeue - no message buffer |  `581.341`|   `11.5766`|    `30.2940`|    `32 B` |

---

### On OSX

#### Host

```ini
OS=macOS Catalina 10.15.5
Intel Core i7-7567U CPU 3.50GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
.NET Core SDK=5.0.100-preview.7.20366.6
  [Host]        : .NET Core 3.1.6, X64 RyuJIT
  .NET Core 3.1 : .NET Core 3.1.6, X64 RyuJIT
  .NET Core 5.0 : .NET Core 5.0.0, X64 RyuJIT
```

#### Results

|                                          Method | .NET | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |-----:|----------:|-----------:|------------:|----------:|
|                                 Message enqueue |  3.1 |    `14.53`|      `0.11`|       `0.10`|        `-`|
|                     Message enqueue and dequeue |  3.1 | `1,649.06`|     `27.14`|      `24.06`|     `40 B`|
| Message enqueue and dequeue - no message buffer |  3.1 | `1,596.43`|     `21.43`|      `19.00`|     `72 B`|
|                                 Message enqueue |  5.0 |     `4.97`|      `0.12`|       `0.14`|        `-`|
|                     Message enqueue and dequeue |  5.0 | `1,656.19`|     `17.30`|      `13.51`|     `43 B`|
| Message enqueue and dequeue - no message buffer |  5.0 | `1,721.16`|     `11.03`|       `9.78`|     `76 B`|

---

### On Ubuntu (through [WSL](https://docs.microsoft.com/en-us/windows/wsl/about))

#### Host

```ini
OS=ubuntu 20.04
Intel Xeon CPU E5-1620 v3 3.50GHz, 1 CPU, 8 logical and 4 physical cores
.NET Core SDK=5.0.100-preview.7.20366.6
  [Host]        : .NET Core 3.1.7, X64 RyuJIT
  .NET Core 3.1 : .NET Core 3.1.7, X64 RyuJIT
```

#### Results

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                                 Message enqueue |    `16.29`|      `0.39`|       `1.16`|        `-`|
|                     Message enqueue and dequeue | `1,580.30`|     `45.17`|     `133.20`|     `15 B`|
| Message enqueue and dequeue - no message buffer | `1,589.26`|     `59.46`|     `174.40`|     `47 B`|

## Implementation Notes

This library relies on [Named Semaphores](https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphore#remarks) To signal the existence of a new message to all message subscribers and to do it across process boundaries. Named semaphores are synchronization constructs accessible across processes.

.NET Core 3.1 and .NET 5 do not support named semaphores on Unix-based OSs (Linux, macOS, etc.). To replicate a named semaphore most efficiently, we are using [Unix Domain Sockets](https://en.wikipedia.org/wiki/Unix_domain_socket) to send signals between processes.

It is worth mentioning that we support multiple signal publishers and receivers; therefore, you will find some logic on Unix to utilize multiple named sockets. We also use a [file system watcher](https://docs.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher) to track the addition and removal of signal publishers (Unix Domain Sockets use backing files).

The domain socket implementation will be replaced with [`System.Threading.Semaphore`](https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphore), once named semaphores are supported on all platforms.

## How to Contribute

- Create a branch from `main`.
- Ensure that all tests pass on Windows, Linux, and OSX.
- Keep the code coverage number above 80% by adding new tests or modifying the existing tests.
- Send a pull request.

## Author

[**Pedram Rezaei**](https://www.linkedin.com/in/pedramrezaei/) is a software architect at Microsoft with years of experience building highly scalable and reliable cloud-native applications for Microsoft.

## What is next

Here are a couple of items that we are working on.

- Complete the support for .NET 5 as soon as the bugs in the current preview version of .NET are addressed by the CLR team
- Create a documentation website
