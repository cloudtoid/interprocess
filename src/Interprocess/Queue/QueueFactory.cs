using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cloudtoid.Interprocess
{
    public sealed class QueueFactory : IQueueFactory
    {
        private readonly ILoggerFactory loggerFactory;

        public QueueFactory()
        {
            loggerFactory = NullLoggerFactory.Instance;
        }

        public QueueFactory(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        }

        public IPublisher CreatePublisher(QueueOptions options)
            => new Publisher(options, loggerFactory);

        public ISubscriber CreateSubscriber(QueueOptions options)
            => new Subscriber(options, loggerFactory);
    }
}
