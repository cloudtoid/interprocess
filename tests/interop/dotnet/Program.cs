using Cloudtoid.Interprocess;
using System.Buffers.Binary;
var mode = args[0];
var options = new QueueOptions(args[1], args[2], 4096);
var count = int.Parse(args[3]);
var factory = new QueueFactory();
byte[] Message(int i) {
    var data = new byte[8 + i % 251];
    BinaryPrimitives.WriteUInt64LittleEndian(data, (ulong)i);
    for (int j = 8; j < data.Length; j++) data[j] = (byte)((i + j) % 251);
    return data;
}
if (mode == "publish") {
    using var p = factory.CreatePublisher(options);
    for (int i = 0; i < count; i++) {
        var data = Message(i);
        while (!p.TryEnqueue(data)) Thread.Yield();
    }
} else {
    using var s = factory.CreateSubscriber(options);
    Console.WriteLine("READY");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    for (int i = 0; i < count; i++) {
        var data = s.Dequeue(timeout.Token);
        if (!data.Span.SequenceEqual(Message(i))) throw new Exception($"Message {i} differs");
    }
}
