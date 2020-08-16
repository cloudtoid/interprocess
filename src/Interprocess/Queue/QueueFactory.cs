using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cloudtoid.Interprocess
{
    public sealed class QueueFactory : IQueueFactory
    {
        private readonly ILoggerFactory loggerFactory;

        public QueueFactory()
            : this(NullLoggerFactory.Instance)
        {
        }

        public QueueFactory(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        }

        /// <summary>
        /// Creates a queue message publisher.
        /// </summary>
        public IPublisher CreatePublisher(QueueOptions options)
            => new Publisher(options, loggerFactory);

        /// <summary>
        /// Creates a queue message subscriber.
        /// </summary>
        public ISubscriber CreateSubscriber(QueueOptions options)
            => new Subscriber(options, loggerFactory);
    }
}
