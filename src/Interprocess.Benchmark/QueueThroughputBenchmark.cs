using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Benchmark
{
    //[SimpleJob(RuntimeMoniker.NetCoreApp31)]
    public class QueueThroughputBenchmark
    {
        private static readonly byte[] message = new byte[50];
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

        [Benchmark]
        public async Task<ReadOnlyMemory<byte>> EnqueueAndDequeueLongMessage()
        {
            await publisher!.TryEnqueueAsync(message, default);
            return await subscriber!.DequeueAsync(default);
        }

        [Benchmark]
        public async Task<ReadOnlyMemory<byte>> EnqueueAndDequeueWrappedMessage()
        {
            await publisher!.TryEnqueueAsync(message, default);
            await subscriber!.DequeueAsync(default);
            await publisher!.TryEnqueueAsync(message, default);
            return await subscriber!.DequeueAsync(default);
        }
    }
}
