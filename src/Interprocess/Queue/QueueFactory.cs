using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cloudtoid.Interprocess
{
    public sealed class QueueFactory : IQueueFactory
    {
        private readonly ILogger<IQueueFactory> logger;

        public QueueFactory()
        {
            logger = NullLogger<IQueueFactory>.Instance;
        }

        public QueueFactory(ILogger<IQueueFactory> logger)
        {
            this.logger = logger ?? NullLogger<IQueueFactory>.Instance;
        }

        public IPublisher CreatePublisher(QueueOptions options)
            => new Publisher(options, logger);

        public ISubscriber CreateSubscriber(QueueOptions options)
            => new Subscriber(options, logger);
    }
}
