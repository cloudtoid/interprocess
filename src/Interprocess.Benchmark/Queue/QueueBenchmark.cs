using System;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Cloudtoid.Interprocess.Benchmark
{
    [SimpleJob(RuntimeMoniker.NetCoreApp50)]
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    [MemoryDiagnoser]
    public class QueueBenchmark
    {
        private static readonly byte[] Message = new byte[] { 100, 110, 120 };
        private static readonly byte[] MessageBuffer = new byte[Message.Length];
#pragma warning disable CS8618
        private IPublisher publisher;
        private ISubscriber subscriber;
#pragma warning restore CS8618

        [GlobalSetup]
        public void Setup()
        {
            var queueFactory = new QueueFactory();
            publisher = queueFactory.CreatePublisher(new QueueOptions("qn", Path.GetTempPath(), 128, true));
            subscriber = queueFactory.CreateSubscriber(new QueueOptions("qn", Path.GetTempPath(), 128, false));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            subscriber.Dispose();
            publisher.Dispose();
        }

        // Expecting that there are NO managed heap allocations.
        [Benchmark(Description = "Message enqueue")]
        public bool Enqueue()
        {
            return publisher!.TryEnqueue(Message);
        }

        [Benchmark(Description = "Message enqueue and dequeue - no message buffer")]
        public async ValueTask<ReadOnlyMemory<byte>> EnqueueDequeue_WithResultArrayAllocationAsync()
        {
            publisher.TryEnqueue(Message);
            return await subscriber.DequeueAsync(default);
        }

        // Expecting that there are NO managed heap allocations.
        [Benchmark(Description = "Message enqueue and dequeue")]
        public async ValueTask<ReadOnlyMemory<byte>> EnqueueAndDequeue_WithPooledResultArrayAsync()
        {
            publisher.TryEnqueue(Message);
            return await subscriber.DequeueAsync(MessageBuffer, default);
        }
    }
}
