# Cloudtoid.Interprocess

Exchange byte messages between processes on the same machine using a shared-memory queue. Multiple publishers and subscribers can connect to the same queue on Windows, Linux, or macOS.

## Install

Requires .NET 10 or later and a 64-bit process.

```sh
dotnet add package Cloudtoid.Interprocess --prerelease
```

## Upgrading to 3.0 alpha

Version 3 is an **alpha prerelease**. APIs and the shared-memory protocol may change between alpha releases.
Drain the queue, stop all participants, and recreate it when upgrading between alpha versions.

Version 3 changes the shared-memory protocol to prevent publisher reservations from overwriting unread
messages after offset wrap. Drain the old queue and upgrade all publishers and subscribers together.
V2 and v3 use separate resources even with the same queue name; queued messages are not migrated.

The MMF stays circular and fixed in size. Logical positions never wrap: after approximately 9.22 exabytes
of reserved bytes (including headers and padding), `TryEnqueue` throws `OverflowException` before changing
the queue. Drain accepted messages and move all participants to a fresh queue.

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

For dependency injection, register the queue services with `services.AddInterprocessQueue()` and resolve `IQueueFactory`.

[Publisher and subscriber samples](https://github.com/cloudtoid/interprocess/tree/main/src/Sample) · [Documentation](https://github.com/cloudtoid/interprocess) · [Report an issue](https://github.com/cloudtoid/interprocess/issues) · [MIT license](https://github.com/cloudtoid/interprocess/blob/main/LICENSE)
