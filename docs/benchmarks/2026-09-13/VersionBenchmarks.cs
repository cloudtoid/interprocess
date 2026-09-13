using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Cloudtoid.Interprocess;

BenchmarkSwitcher.FromAssembly(typeof(RoundTrip).Assembly).Run(args);

[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 2, warmupCount: 3, iterationCount: 8)]
[IterationTime(250)]
[MemoryDiagnoser]
public class RoundTrip
{
    [Params(8, 50)]
    public int PayloadBytes { get; set; }

    private IPublisher publisher = null!;
    private ISubscriber subscriber = null!;
    private byte[] payload = null!;
    private byte[] destination = null!;

    [GlobalSetup]
    public void Setup()
    {
        var factory = new QueueFactory();
        var options = new QueueOptions(Guid.NewGuid().ToString("N")[..16], PayloadBytes == 50 ? 120 : 65536);
        publisher = factory.CreatePublisher(options);
        subscriber = factory.CreateSubscriber(options);
        payload = Enumerable.Range(0, PayloadBytes).Select(i => (byte)i).ToArray();
        destination = new byte[PayloadBytes];
    }

    [Benchmark]
    public ReadOnlyMemory<byte> SendAndReceive()
    {
        if (!publisher.TryEnqueue(payload))
            throw new InvalidOperationException("The benchmark queue is unexpectedly full.");

        return subscriber.Dequeue(destination, CancellationToken.None);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        subscriber.Dispose();
        publisher.Dispose();
    }
}

[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 2, warmupCount: 3, iterationCount: 8)]
[IterationTime(250)]
public class ConcurrentDelivery
{
    private const int Count = 32768;
    private const int ReaderCount = 4;
    private static readonly byte[] Payload = [0, 1, 2, 3, 4, 5, 6, 7];
    private IPublisher[] publishers = [];
    private ISubscriber[] subscribers = [];

    [Params(1, 4)]
    public int PublisherCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var factory = new QueueFactory();
        var options = new QueueOptions(Guid.NewGuid().ToString("N")[..16], 65536);
        publishers = Enumerable.Range(0, PublisherCount).Select(_ => factory.CreatePublisher(options)).ToArray();
        subscribers = Enumerable.Range(0, ReaderCount).Select(_ => factory.CreateSubscriber(options)).ToArray();
    }

    [Benchmark(OperationsPerInvoke = Count)]
    public void Deliver()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var start = new ManualResetEventSlim();
        var workers = new List<Task>();
        foreach (var subscriber in subscribers)
        {
            workers.Add(Task.Factory.StartNew(() =>
            {
                var buffer = new byte[8];
                start.Wait(cancellation.Token);
                for (var i = 0; i < Count / ReaderCount; i++)
                {
                    var message = subscriber.Dequeue(buffer, cancellation.Token);
                    if (!message.Span.SequenceEqual(Payload))
                        throw new InvalidOperationException("The benchmark received a corrupted message.");
                }
            }, cancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default));
        }

        foreach (var publisher in publishers)
        {
            workers.Add(Task.Factory.StartNew(() =>
            {
                start.Wait(cancellation.Token);
                for (var i = 0; i < Count / PublisherCount; i++)
                {
                    while (!publisher.TryEnqueue(Payload))
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        Thread.Yield();
                    }
                }
            }, cancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default));
        }

        start.Set();
        Task.WaitAll(workers.ToArray());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var subscriber in subscribers)
            subscriber.Dispose();
        foreach (var publisher in publishers)
            publisher.Dispose();
    }
}
