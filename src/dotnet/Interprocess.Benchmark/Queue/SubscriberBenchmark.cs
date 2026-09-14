using BenchmarkDotNet.Attributes;

namespace Cloudtoid.Interprocess.Benchmark;

[ShortRunJob]
[MarkdownExporterAttribute.GitHub]
public class SubscriberBenchmark
{
    private const int MessageCount = 65536;
    private readonly QueueFactory factory = new();
    private readonly QueueOptions options = new("subscriber-bench", 65536);
    private IPublisher publisher = null!;
    private ISubscriber[] subscribers = [];

    [Params(1, 4)]
    public int SubscriberCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        publisher = factory.CreatePublisher(options);
        subscribers = Enumerable.Range(0, SubscriberCount).Select(_ => factory.CreateSubscriber(options)).ToArray();
    }

    // Retain the transient queue throughout measurement; close only after all timed work.
    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var subscriber in subscribers)
            subscriber.Dispose();

        publisher.Dispose();
    }

    [Benchmark(OperationsPerInvoke = MessageCount)]
    public async Task ReceiveConcurrentlyAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var readers = new Task[subscribers.Length];
        for (var reader = 0; reader < subscribers.Length; reader++)
        {
            var subscriber = subscribers[reader];
            readers[reader] = Task.Factory.StartNew(
                () =>
                {
                    var buffer = new byte[8];
                    for (var i = 0; i < MessageCount / SubscriberCount; i++)
                        subscriber.Dequeue(buffer, cancellation.Token);
                },
                cancellation.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        for (var i = 0; i < MessageCount; i++)
        {
            while (!publisher.TryEnqueue("message!"u8))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Thread.Yield();
            }
        }

        await Task.WhenAll(readers);
    }
}