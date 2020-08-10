using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess.Benchmark
{
    public sealed class Program
    {
        static void Main()
        {
            _ = BenchmarkRunner.Run<QueueBenchmark>();
        }
    }

    [SimpleJob(RuntimeMoniker.NetCoreApp31)]
    public class QueueBenchmark
    {
        private static readonly byte[] shortMessage = new byte[] { 100, 110, 120 };
        private static readonly byte[] longMessage = new byte[50];
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

        //[Benchmark]
        //public async Task<ReadOnlyMemory<byte>> EnqueueAndDequeueShortMessage()
        //{
        //    await publisher!.TryEnqueueAsync(longMessage, default);
        //    return await subscriber!.DequeueAsync(default);
        //}

        //[Benchmark]
        //public async Task<ReadOnlyMemory<byte>> EnqueueAndDequeueLongMessage()
        //{
        //    await publisher!.TryEnqueueAsync(longMessage, default);
        //    return await subscriber!.DequeueAsync(default);
        //}

        [Benchmark]
        public async Task<ReadOnlyMemory<byte>> EnqueueAndDequeueMessageWrap()
        {
            try
            {
                await publisher!.TryEnqueueAsync(longMessage, default);
                await subscriber!.DequeueAsync(default);
                await publisher!.TryEnqueueAsync(longMessage, default);
                return await subscriber!.DequeueAsync(default);
            }
            catch
            {
                Console.WriteLine("Failed");
                throw;
            }
        }
    }
}
