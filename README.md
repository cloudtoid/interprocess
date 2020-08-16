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



## Contribute

## Implementation Notes

## Author

[**Pedram Rezaei**](https://www.linkedin.com/in/pedramrezaei/): Pedram is a software architect at Microsoft with years of experience building highly scalable and reliable cloud-native applications for Microsoft.



## Notes:
Command to run the benchmark: `dotnet run Interprocess.Benchmark.csproj --configuration Release --framework net5.0 --runtimes net5.0 netcoreapp3.1`

