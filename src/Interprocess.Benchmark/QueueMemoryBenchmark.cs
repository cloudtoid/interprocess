using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Benchmark
{
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    [MemoryDiagnoser]
    public class QueueMemoryBenchmark
    {
        private static readonly byte[] message = new byte[] { 100, 110, 120 };
        private static readonly byte[] messageBuffer = new byte[message.Length];
        private IPublisher? publisher;
        private ISubscriber? subscriber;

        [GlobalSetup]
        public void Setup()
        {
            var queueFactory = new QueueFactory();
            publisher = queueFactory.CreatePublisher(new QueueOptions("qn", Environment.CurrentDirectory, 128, true));
            subscriber = queueFactory.CreateSubscriber(new QueueOptions("qn", Environment.CurrentDirectory, 128, false));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            using (publisher)
            using (subscriber) { }
        }

        // Expecting that there are NO managed heap allocations.
        [Benchmark]
        public bool Enqueue()
        {
            return publisher!.TryEnqueue(message, default);
        }

        [Benchmark]
        public async ValueTask<ReadOnlyMemory<byte>> EnqueueDequeue_WithResultArrayAllocation()
        {
            publisher!.TryEnqueue(message, default);
            return await subscriber!.DequeueAsync(default);
        }

        [Benchmark]
        public async ValueTask<ReadOnlyMemory<byte>> EnqueueAndDequeue_WithPooledResultArray()
        {
            publisher!.TryEnqueue(message, default);
            return await subscriber!.DequeueAsync(messageBuffer, default);
        }
    }
}
