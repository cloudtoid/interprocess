using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Cloudtoid.Interprocess.Benchmark;

[SimpleJob(RuntimeMoniker.Net10_0)]
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class EnqueueBenchmark
{
    private const int MessageCount = 320000;
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
        publisher = queueFactory.CreatePublisher(new QueueOptions("qn", Path.GetTempPath(), MessageCount * 16));
        subscriber = queueFactory.CreateSubscriber(new QueueOptions("qn", Path.GetTempPath(), MessageCount * 16));
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
        for (var i = 0; i < MessageCount; i++)
        {
            if (!subscriber.TryDequeue(MessageBuffer, out _))
                throw new InvalidOperationException("The benchmark did not enqueue the expected number of messages.");
        }
    }

    // Expecting that there are NO managed heap allocations.
    [Benchmark(Description = "Message enqueue", OperationsPerInvoke = MessageCount)]
    public void Enqueue()
    {
        for (var i = 0; i < MessageCount; i++)
        {
            if (!publisher.TryEnqueue(Message))
                throw new InvalidOperationException("The benchmark queue is full.");
        }
    }
}