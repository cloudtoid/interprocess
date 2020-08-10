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
        public ValueTask<bool> Enqueue()
        {
            return publisher!.TryEnqueueAsync(message, default);
        }

        [Benchmark]
        public async ValueTask<ReadOnlyMemory<byte>> EnqueueDequeue_WithResultArrayAllocation()
        {
            await publisher!.TryEnqueueAsync(message, default);
            return await subscriber!.DequeueAsync(default);
        }

        [Benchmark]
        public async ValueTask<ReadOnlyMemory<byte>> EnqueueAndDequeue_WithPooledResultArray()
        {
            await publisher!.TryEnqueueAsync(message, default);
            return await subscriber!.DequeueAsync(messageBuffer, default);
        }
    }
}
