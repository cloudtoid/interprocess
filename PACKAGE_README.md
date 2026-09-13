# Cloudtoid.Interprocess

Exchange byte messages between processes on the same machine using a shared-memory queue. Multiple publishers and subscribers can connect to the same queue on Windows, Linux, or macOS.

## Install

Requires .NET 10 or later and a 64-bit process.

```sh
dotnet add package Cloudtoid.Interprocess
```

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
