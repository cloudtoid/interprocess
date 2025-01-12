using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Cloudtoid.Interprocess.Benchmark;

[SimpleJob(RuntimeMoniker.Net90)]
[MarkdownExporterAttribute.GitHub]
public class QueueExtendedBenchmark
{
    private static readonly byte[] Message = new byte[50];
    private static readonly byte[] MessageBuffer = new byte[Message.Length];
#pragma warning disable CS8618
    private IPublisher publisher;
    private ISubscriber subscriber;
#pragma warning restore CS8618

    [GlobalSetup]
    public void Setup()
    {
        var queueFactory = new QueueFactory();
        publisher = queueFactory.CreatePublisher(new QueueOptions("qn", Path.GetTempPath(), 128));
        subscriber = queueFactory.CreateSubscriber(new QueueOptions("qn", Path.GetTempPath(), 128));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        subscriber.Dispose();
        publisher.Dispose();
    }

    [Benchmark(Description = "Message enqueue and dequeue - long message")]
    public ReadOnlyMemory<byte> EnqueueDequeue_LongMessage()
    {
        if (!publisher.TryEnqueue(Message))
            throw new Exception("Failed to enqueue");

        return subscriber.Dequeue(MessageBuffer, default);
    }

    [Benchmark(Description = "Message enqueue and dequeue - wrapped message in circular buffer")]
    public ReadOnlyMemory<byte> EnqueueDequeue_WrappedMessages()
    {
        if (!publisher.TryEnqueue(Message))
            throw new Exception("Failed to enqueue");

        subscriber.Dequeue(MessageBuffer, default);

        if (!publisher.TryEnqueue(Message))
            throw new Exception("Failed to enqueue");

        return subscriber.Dequeue(MessageBuffer, default);
    }
}