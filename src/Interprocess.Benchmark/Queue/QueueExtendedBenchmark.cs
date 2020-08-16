using System;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Cloudtoid.Interprocess.Benchmark
{
    [SimpleJob(RuntimeMoniker.NetCoreApp50)]
    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
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
            publisher = queueFactory.CreatePublisher(new QueueOptions("qn", Path.GetTempPath(), 128, true));
            subscriber = queueFactory.CreateSubscriber(new QueueOptions("qn", Path.GetTempPath(), 128, false));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            using (publisher)
            using (subscriber) { }
        }

        [Benchmark]
        public async Task<ReadOnlyMemory<byte>> EnqueueDequeue_LongMessageAsync()
        {
            publisher.TryEnqueue(Message);
            return await subscriber.DequeueAsync(MessageBuffer, default);
        }

        // when a message is wrapped in the circular buffer
        [Benchmark]
        public async Task<ReadOnlyMemory<byte>> EnqueueDequeue_WrappedMessagesAsync()
        {
            publisher.TryEnqueue(Message);
            await subscriber.DequeueAsync(MessageBuffer, default);
            publisher.TryEnqueue(Message);
            return await subscriber.DequeueAsync(MessageBuffer, default);
        }
    }
}
