using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Cloudtoid.Interprocess.Benchmark;

[SimpleJob(RuntimeMoniker.Net10_0)]
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class QueueExtendedBenchmark
{
    private static readonly byte[] Message = new byte[50];
    private static readonly byte[] MessageBuffer = new byte[Message.Length];
#pragma warning disable CS8618
    private IPublisher publisher;
    private ISubscriber subscriber;
#pragma warning restore CS8618

    [GlobalSetup(Target = nameof(EnqueueDequeue_LongMessage))]
    public void Setup() => SetupQueue(128);

    [GlobalSetup(Target = nameof(EnqueueDequeue_WrappedMessages))]
    public void SetupWrapped() => SetupQueue(120);

    // Retain the transient queue throughout measurement; close only after all timed work.
    [GlobalCleanup]
    public void Cleanup()
    {
        subscriber.Dispose();
        publisher.Dispose();
    }

    [Benchmark(Description = "Send + receive - long message")]
    public ReadOnlyMemory<byte> EnqueueDequeue_LongMessage()
    {
        if (!publisher.TryEnqueue(Message))
            throw new Exception("Failed to enqueue");

        return subscriber.Dequeue(MessageBuffer, default);
    }

    // A padded message occupies 64 bytes. A 120-byte ring makes message bodies cross
    // the end of the buffer; a 128-byte ring only cycles between aligned slots.
    [Benchmark(Description = "Send + receive - ring-wrap workload", OperationsPerInvoke = 2)]
    public ReadOnlyMemory<byte> EnqueueDequeue_WrappedMessages()
    {
        if (!publisher.TryEnqueue(Message))
            throw new Exception("Failed to enqueue");

        subscriber.Dequeue(MessageBuffer, default);

        if (!publisher.TryEnqueue(Message))
            throw new Exception("Failed to enqueue");

        return subscriber.Dequeue(MessageBuffer, default);
    }

    private void SetupQueue(long capacity)
    {
        var queueFactory = new QueueFactory();
        publisher = queueFactory.CreatePublisher(new QueueOptions("qn", Path.GetTempPath(), capacity));
        subscriber = queueFactory.CreateSubscriber(new QueueOptions("qn", Path.GetTempPath(), capacity));
    }
}