# Cloudtoid.Interprocess

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

Each queue supports up to 2,048 connected publisher objects. Its shared publisher table uses 256 KiB in addition to the message capacity and header/alignment storage. Dispose participants when finished; the queue remains available while any participant is connected.

For dependency injection, register the queue services with `services.AddInterprocessQueue()` and resolve `IQueueFactory`.

## Limits

Queue names must be nonempty and contain no slash or NUL. Windows also rejects backslashes; Unix permits them for compatibility. The maximum is 24 UTF-8 bytes on macOS and 245 on Linux; use at most 24 bytes for portable names.

## Queue lifetime

The queue is transient IPC storage. Once all publishers and subscribers are gone, unread messages are lost; reopening the same name creates a fresh, empty queue. Keep a subscriber connected before a short-lived publisher exits. Recovery after a process exits can discard queued messages, including completed messages behind an unfinished reservation. Paused live operations are not reclaimed merely because a timeout passes. Destination buffers must be large enough to avoid truncating a consumed message.

[Publisher and subscriber samples](https://github.com/cloudtoid/interprocess/tree/main/src/dotnet/Sample) · [Documentation](https://github.com/cloudtoid/interprocess) · [Report an issue](https://github.com/cloudtoid/interprocess/issues) · [MIT license](https://github.com/cloudtoid/interprocess/blob/main/LICENSE)