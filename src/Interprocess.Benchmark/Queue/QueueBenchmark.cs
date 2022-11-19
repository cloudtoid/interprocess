using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Cloudtoid.Interprocess.Benchmark
{
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    [SimpleJob(RuntimeMoniker.Net60)]
    [SimpleJob(RuntimeMoniker.Net70)]
    [MemoryDiagnoser]
    [MarkdownExporterAttribute.GitHub]
    public class QueueBenchmark
    {
        private static readonly byte[] Message = new byte[] { 100, 110, 120 };
        private static readonly Memory<byte> MessageBuffer = new byte[Message.Length];
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

        [Benchmark(Description = "Message enqueue and dequeue - no message buffer")]
        public ReadOnlyMemory<byte> EnqueueDequeue_WithResultArrayAllocation()
        {
            if (!publisher.TryEnqueue(Message))
                throw new Exception("Failed to enqueue");

            return subscriber.Dequeue(default);
        }

        // Expecting that there are NO managed heap allocations.
        [Benchmark(Description = "Message enqueue and dequeue")]
        public ReadOnlyMemory<byte> EnqueueAndDequeue_WithPooledResultArray()
        {
            if (!publisher.TryEnqueue(Message))
                throw new Exception("Failed to enqueue");

            return subscriber.Dequeue(MessageBuffer, default);
        }
    }
}
