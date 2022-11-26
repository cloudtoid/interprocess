using System.Threading;
using Cloudtoid.Interprocess;
using Microsoft.Extensions.Logging;

namespace Publisher
{
    internal class Program
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
                messageCapacityInBytes: 1024 * 1024);

            using var publisher = factory.CreatePublisher(options);

            // Enqueue messages

            byte i = 0;
            while (true)
            {
                logger.LogInformation("Enqueue #" + i);

                if (publisher.TryEnqueue(new byte[] { i }))
                    i++;

                Thread.Yield();
            }
        }
    }
}
