using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Benchmark
{
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    public class QueueBenchmark
    {
        private static readonly byte[] message = new byte[50];
        private static readonly byte[] messageBuffer = new byte[message.Length];
#pragma warning disable CS8618
        private IPublisher publisher;
        private ISubscriber subscriber;
#pragma warning restore CS8618

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
        public async Task<ReadOnlyMemory<byte>> EnqueueDequeue_LongMessage()
        {
            publisher.TryEnqueue(message);
            return await subscriber.DequeueAsync(messageBuffer, default);
        }

        // when a message is wrapped in the circular buffer
        [Benchmark]
        public async Task<ReadOnlyMemory<byte>> EnqueueDequeue_WrappedMessages()
        {
            publisher.TryEnqueue(message);
            await subscriber.DequeueAsync(messageBuffer, default);
            publisher.TryEnqueue(message);
            return await subscriber.DequeueAsync(messageBuffer, default);
        }
    }
}
