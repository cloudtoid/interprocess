# Cloudtoid.Interprocess

[API guide](https://cloudtoid.com/docs/dotnet/) · [Queue concepts](https://cloudtoid.com/docs/concepts/) · [Website](https://cloudtoid.com)

Exchange byte messages between processes on the same machine using a shared-memory queue. Multiple publishers and subscribers can connect to the same queue on Windows, Linux, or macOS.

## Install

Requires .NET 10 or later and a 64-bit process.

```sh
dotnet add package Cloudtoid.Interprocess
```

## Faster with v3

Version 3 coalesces notifications to reduce operating-system calls and speed up message delivery. See the [version comparison and benchmarks](https://github.com/cloudtoid/interprocess#performance).

Version 3 uses a new shared-memory format. Drain the queue, stop all participants, and upgrade them together using a fresh queue.

## Example

```csharp
using Cloudtoid.Interprocess;

var factory = new QueueFactory();
var options = new QueueOptions("example-queue", capacity: 1024 * 1024);

using var publisher = factory.CreatePublisher(options);
using var subscriber = factory.CreateSubscriber(options);

byte[] payload = [1, 2, 3];
byte[] buffer = new byte[256];

if (publisher.TryEnqueue(payload) &&
    subscriber.TryDequeue(buffer, out var message))
{
    Console.WriteLine($"Received {message.Length} bytes");
}
```

For separate processes, create the publisher and subscriber in their respective programs using the same queue name, storage path, and capacity. The example uses the default temporary directory. Handle unsuccessful enqueue/dequeue attempts according to your application's retry and cancellation needs.

For dependency injection, register `services.AddInterprocessQueue()` and resolve `IQueueFactory`.

Queues are transient: once all publishers and subscribers are gone, unread messages are lost. Keep at least one endpoint connected throughout a handoff between processes. See the [API guide](https://cloudtoid.com/docs/dotnet/) for waiting, errors, ownership, and limits.
