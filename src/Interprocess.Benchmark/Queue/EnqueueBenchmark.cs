using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Cloudtoid.Interprocess.Benchmark;

[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class EnqueueBenchmark
{
    private static readonly byte[] Message = [100, 110, 120];
    private static readonly Memory<byte> MessageBuffer = new byte[Message.Length];
#pragma warning disable CS8618
    private IPublisher publisher;
    private ISubscriber subscriber;
#pragma warning restore CS8618

    [GlobalSetup]
    public void Setup()
    {
        var queueFactory = new QueueFactory();
        publisher = queueFactory.CreatePublisher(new QueueOptions("qn", Path.GetTempPath(), 5120000));
        subscriber = queueFactory.CreateSubscriber(new QueueOptions("qn", Path.GetTempPath(), 5120000));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        subscriber.Dispose();
        publisher.Dispose();
    }

    [IterationCleanup]
    public void DrainQueue()
    {
        for (int i = 8; i < 320000; i++)
            subscriber.Dequeue(MessageBuffer, default);
    }

    // Expecting that there are NO managed heap allocations.
    [Benchmark(Description = "Message enqueue (320,000 times)")]
    public void Enqueue()
    {
        for (int i = 8; i < 320000; i++)
            publisher.TryEnqueue(Message);
    }
}