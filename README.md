<a href="https://github.com/cloudtoid"><img src="https://raw.githubusercontent.com/cloudtoid/assets/master/logos/cloudtoid-black-red.png" width="100"></a>

# Interprocess

![](https://github.com/cloudtoid/interprocess/workflows/publish/badge.svg) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/cloudtoid/url-patterns/blob/master/LICENSE) ![https://www.nuget.org/packages/Cloudtoid.Interprocess/](https://img.shields.io/nuget/vpre/Cloudtoid.Interprocess) ![](https://img.shields.io/badge/.net%20core-%3E%203.1.0-blue)

**Cloudtoid Interprocess** is a cross-platform shared memory queue for fast communication between processes ([Interprocess Communication or IPC](https://en.wikipedia.org/wiki/Inter-process_communication)). It uses a shared memory mapped file for extremly fast and efficient communication between processes.

- **Fast**: It is *extremely* fast.
- **Cross-platform**: It supports Windows, and Unix-based operating systems such as Linux, [OSX](https://en.wikipedia.org/wiki/MacOS), and [FreeBSD](https://www.freebsd.org/).
- **API**: Provides a simple and intuitive API to enqueue/send and dequeue/receive messages.
- **Multiple publishers and subscribers**: It supports multiple publishers and subscribers to a shared queue.
- **Efficient**: Sending and receiving messages is *almost* heap memory allocation free reducing garbage collections.
- **.NET**: Supports .NET Core 3.1 and .NET 5.
- **Developer**: Developed by folks at Microsoft.

## NuGet Package

The NuGet package for this library is published [here](https://www.nuget.org/packages/Cloudtoid.Interprocess/).

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
await subscriber.TryDequeueAsync(messageBuffer, cancellationToken, out var msg);
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

## Performance

A lot has gone into optimizing the implementation of this library. It is also mostly heap memory allocation free reducing the need for garbage collection induced pauses.

To benchmark the performance and the memory usage, we use [BenchmarkDotNet](https://benchmarkdotnet.org/). Here are the results on fairly slow dev machines:

|                                            Method |       Description |
|-------------------------------------------------- |-------------- |
|                                 'Message enqueue' | Benchmarks the performance of enqueuing a message |
|                     'Message enqueue and dequeue' | Benchmarks the performance of sending a message to a client. It is inclusive of the time taken to enqueue and dequeue a message |
| 'Message enqueue and dequeue - no message buffer' | Benchmarks the performance of sending a message to a client. It is inclusive of the time taken to enqueue and dequeue a message, as well as, allocating memory for the received message |

You can replicate the results by running the following command:

```cmd
dotnet run Interprocess.Benchmark.csproj --configuration Release
```

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

|                                            Method |       Runtime |       Mean |      Error |     StdDev | Allocated |
|-------------------------------------------------- |-------------- |-----------:|-----------:|-----------:|----------:|
|                                 'Message enqueue' | .NET Core 3.1 |   6.138 ns |  0.1641 ns |  0.3315 ns |         - |
|                     'Message enqueue and dequeue' | .NET Core 3.1 | 584.651 ns | 11.5850 ns | 23.6650 ns |         - |
| 'Message enqueue and dequeue - no message buffer' | .NET Core 3.1 | 581.341 ns | 11.5766 ns | 30.2940 ns |      32 B |

### On OSX

Host:

```ini
OS=macOS Catalina 10.15.5
Intel Core i7-7567U CPU 3.50GHz (Kaby Lake), 1 CPU, 4 logical and 2 physical cores
.NET Core SDK=5.0.100-preview.7.20366.6
  [Host]        : .NET Core 3.1.6, X64 RyuJIT
  .NET Core 3.1 : .NET Core 3.1.6, X64 RyuJIT
  .NET Core 5.0 : .NET Core 5.0.0, X64 RyuJIT
```

|                                            Method |       Runtime |         Mean |      Error |     StdDev | Allocated |
|-------------------------------------------------- |-------------- |-------------:|-----------:|-----------:|----------:|
|                                 'Message enqueue' | .NET Core 3.1 |    14.539 ns |  0.1102 ns |  0.1030 ns |         - |
|                     'Message enqueue and dequeue' | .NET Core 3.1 | 1,649.060 ns | 27.1430 ns | 24.0616 ns |      40 B |
| 'Message enqueue and dequeue - no message buffer' | .NET Core 3.1 | 1,596.437 ns | 21.4398 ns | 19.0059 ns |      72 B |
|                                 'Message enqueue' | .NET Core 5.0 |     4.973 ns |  0.1273 ns |  0.1466 ns |         - |
|                     'Message enqueue and dequeue' | .NET Core 5.0 | 1,656.197 ns | 17.3074 ns | 13.5125 ns |      43 B |
| 'Message enqueue and dequeue - no message buffer' | .NET Core 5.0 | 1,721.164 ns | 11.0354 ns |  9.7826 ns |      76 B |

### On Ubuntu (through [WSL](https://docs.microsoft.com/en-us/windows/wsl/about))

```ini
OS=ubuntu 20.04
Intel Xeon CPU E5-1620 v3 3.50GHz, 1 CPU, 8 logical and 4 physical cores
.NET Core SDK=5.0.100-preview.7.20366.6
  [Host]        : .NET Core 3.1.7, X64 RyuJIT
  .NET Core 3.1 : .NET Core 3.1.7, X64 RyuJIT
```

|                                            Method |       Runtime |         Mean |      Error |      StdDev | Allocated |
|-------------------------------------------------- |-------------- |-------------:|-----------:|------------:|----------:|
|                                 'Message enqueue' | .NET Core 3.1 |    16.298 ns |  0.3997 ns |   1.1660 ns |         - |
|                     'Message enqueue and dequeue' | .NET Core 3.1 | 1,580.302 ns | 45.1770 ns | 133.2054 ns |      15 B |
| 'Message enqueue and dequeue - no message buffer' | .NET Core 3.1 | 1,589.269 ns | 59.4680 ns | 174.4092 ns |      47 B |

## Implementation Notes

To signal the existence of a new message to all message subscribers and do it across process boundaries, we use a [Named Semaphore](https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphore#remarks). Named semaphores are synchronization constructs accessible across processes.

.NET Core 3.1  and .NET 5 do not have support for named semaphores on Unix based OSs (Linux, macOS, etc.). To replicate a named semaphore in the most efficient possible way, we are using Unix Domain Sockets to send signals between processes.

It is worth mentioning that we support multiple signal publishers and receivers; therefore, you will find some logic on Unix to utilize multiple named sockets. We also use a file system watcher to keep track of the addition and removal of signal publishers (Unix Domain Sockets use backing files).

The domain socket implementation will be replaced with [`System.Threading.Semaphore`](https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphore) once named semaphores are supported on all platforms.

## Contribute

## Author

[**Pedram Rezaei**](https://www.linkedin.com/in/pedramrezaei/): Pedram is a software architect at Microsoft with years of experience building highly scalable and reliable cloud-native applications for Microsoft.



## Notes:
Command to run the benchmark: `dotnet run Interprocess.Benchmark.csproj --configuration Release --framework net5.0 --runtimes net5.0 netcoreapp3.1`

