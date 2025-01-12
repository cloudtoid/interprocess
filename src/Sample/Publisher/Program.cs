﻿using Cloudtoid.Interprocess;
using Microsoft.Extensions.Logging;

namespace Publisher;

internal static partial class Program
{
    internal static void Main()
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

        byte i = 0;
        while (true)
        {
            LogEnqueue(logger, i);

            if (publisher.TryEnqueue([i]))
                i++;

            Thread.Yield();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Enqueue #{i}")]
    private static partial void LogEnqueue(ILogger logger, int i);
}