using System.Threading.Tasks;
using Cloudtoid.Interprocess;
using Microsoft.Extensions.Logging;

namespace Subscriber
{
    internal class Program
    {
        internal static async Task Main()
        {
            // Set up an optional logger factory to redirect the traces to he console

            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger("Subscriber");

            // Create the queue factory. If you are not interested in tracing the internals of
            // the queue then don't pass in a loggerFactory

            var factory = new QueueFactory(loggerFactory);

            // Create a message queue publisher

            var options = new QueueOptions(
                queueName: "sample-queue",
                bytesCapacity: 1024 * 1024);

            using var subscriber = factory.CreateSubscriber(options);

            // Dequeue messages
            var messageBuffer = new byte[1];

            while (true)
            {
                if (await subscriber.TryDequeueAsync(messageBuffer, default, out var message))
                    logger.LogInformation("Dequeue #" + messageBuffer[0]);
            }
        }
    }
}
