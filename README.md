<a href="https://github.com/cloudtoid"><img src="https://raw.githubusercontent.com/cloudtoid/assets/master/logos/cloudtoid-black-red.png" width="100"></a>

# Interprocess

![](https://github.com/cloudtoid/interprocess/workflows/publish/badge.svg) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/cloudtoid/url-patterns/blob/master/LICENSE) ![https://www.nuget.org/packages/Cloudtoid.Interprocess/](https://img.shields.io/nuget/vpre/Cloudtoid.Interprocess) ![](https://img.shields.io/badge/.net%20core-%3E%203.1.0-blue)

**Cloudtoid Interprocess** is a cross-platform shared memory queue for fast communication between processes ([Interprocess Communication or IPC](https://en.wikipedia.org/wiki/Inter-process_communication)). It uses a shared memory-mapped file for extremely fast and efficient communication between processes and it is used internally by Microsoft.

- [**Fast**](#performance): It is *extremely* fast.
- **Cross-platform**: It supports Windows, and Unix-based operating systems such as Linux, [OSX](https://en.wikipedia.org/wiki/MacOS), and [FreeBSD](https://www.freebsd.org/).
- [**API**](#Usage): Provides a simple and intuitive API to enqueue/send and dequeue/receive messages.
- **Multiple publishers and subscribers**: It supports multiple publishers and subscribers to a shared queue.
- [**Efficient**](#performance): Sending and receiving messages is almost heap memory allocation free reducing garbage collections.
- [**Developer**](#Author): Developed by a guy at Microsoft.

## NuGet Package

The NuGet package for this library is published [here](https://www.nuget.org/packages/Cloudtoid.Interprocess/).

> Note: To improve performance, this library only supports 64-bit CLR with 64-bit processor architectures. Attempting to use this library on 32-bit processors, 32-bit operating systems, or on [WOW64](https://en.wikipedia.org/wiki/WoW64) may throw a `NotSupportedException`.

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

**Summary**: In average, enqueuing a message is about `~10 ns` and a full enqueue followed by a dequeue takes roughly `~500 ns` on Windows and OSX, and `850 ms` on linux.

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

Host:

```ini
OS=Windows 10.0.19041.450
Intel Xeon CPU E5-1620 v3 3.50GHz, 1 CPU, 8 logical and 4 physical cores
.NET Core SDK=3.1.401
  [Host]        : .NET Core 3.1.7, X64 RyuJIT
  .NET Core 3.1 : .NET Core 3.1.7, X64 RyuJIT
```

Results:

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                                 Message enqueue |    `6.138`|    `0.1641`|     `0.3315`|       `-` |
|                     Message enqueue and dequeue |  `584.651`|   `11.5850`|    `23.6650`|       `-` |
| Message enqueue and dequeue - no message buffer |  `581.341`|   `11.5766`|    `30.2940`|    `32 B` |

---

### On OSX

Host:

```ini
OS=macOS Catalina 10.15.6
Intel Core i5-8279U CPU 2.40GHz (Coffee Lake), 1 CPU, 8 logical and 4 physical cores
.NET Core SDK=3.1.401
  [Host]        : .NET Core 3.1.7, X64 RyuJIT
  .NET Core 3.1 : .NET Core 3.1.7, X64 RyuJIT
```

Results:

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                                 Message enqueue |    `14.19`|      `0.05`|       `0.04`|        `-`|
|                     Message enqueue and dequeue |   `666.10`|     `10.91`|      `10.20`|        `-`|
| Message enqueue and dequeue - no message buffer |   `689.33`|     `13.38`|      `15.41`|     `32 B`|

---

### On Ubuntu (through [WSL](https://docs.microsoft.com/en-us/windows/wsl/about))

Host:

```ini
OS=ubuntu 20.04
Intel Xeon CPU E5-1620 v3 3.50GHz, 1 CPU, 8 logical and 4 physical cores
.NET Core SDK=5.0.100-preview.7.20366.6
  [Host]        : .NET Core 3.1.7, X64 RyuJIT
  .NET Core 3.1 : .NET Core 3.1.7, X64 RyuJIT
```

Results:

|                                          Method | Mean (ns) | Error (ns) | StdDev (ns) | Allocated |
|------------------------------------------------ |----------:|-----------:|------------:|----------:|
|                                 Message enqueue |    `16.61`|     `0.364`|       `1.01`|        `-`|
|                     Message enqueue and dequeue |   `898.97`|     `26.12`|      `77.03`|        `-`|
| Message enqueue and dequeue - no message buffer |   `925.49`|     `21.47`|      `62.98`|     `32 B`|

## Implementation Notes

This library relies on [Named Semaphores](https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphore#remarks) To signal the existence of a new message to all message subscribers and to do it across process boundaries. Named semaphores are synchronization constructs accessible across processes.

.NET Core 3.1 and .NET 5 do not support named semaphores on Unix-based OSs (Linux, macOS, etc.). Instead we are using P/Invoke and rely on operating system's POSIX semaphore implementation. ([Linux](src/interprocess/semaphore/linux/interop.cs) and [MacOS](src/interprocess/semaphore/macos/interop.cs) implementations).

This implementation will be replaced with [`System.Threading.Semaphore`](https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphore) once .NET adds support for named semaphores on all platforms.

## How to Contribute

- Create a branch from `main`.
- Ensure that all tests pass on Windows, Linux, and OSX.
- Keep the code coverage number above 80% by adding new tests or modifying the existing tests.
- Send a pull request.

## Author

[**Pedram Rezaei**](https://www.linkedin.com/in/pedramrezaei/) is a software architect at Microsoft with years of experience building highly scalable and reliable cloud-native applications for Microsoft.

## What is next

Here are a couple of items that we are working on.

- Create a documentation website
