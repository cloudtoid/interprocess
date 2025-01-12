using Cloudtoid.Interprocess;
using Microsoft.Extensions.Logging;

namespace Publisher;

internal static partial class Program
{
    internal static async Task Main()
    {
        // Set up an optional logger factory to redirect the traces to he console

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Publisher");

        // Create the queue factory. If you are not interested in tracing the internals of
        // the queue then don't pass in a loggerFactory

        var factory = new QueueFactory(loggerFactory);

        // Create a message queue publisher

        var options = new QueueOptions(
            queueName: "sample-queue",
            capacity: 1024 * 1024);

        using var publisher = factory.CreatePublisher(options);

        // Enqueue messages

        int i = 0;
        while (true)
        {
            if (publisher.TryEnqueue([(byte)(i % 256)]))
                LogEnqueue(logger, i++);
            else
                await Task.Delay(100);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Enqueue #{i}")]
    private static partial void LogEnqueue(ILogger logger, int i);
}