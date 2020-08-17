using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Cloudtoid.Contract;

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
            Util.Ensure64Bit();
            this.loggerFactory = CheckValue(loggerFactory, nameof(loggerFactory));
        }

        /// <summary>
        /// Creates a queue message publisher.
        /// </summary>
        public IPublisher CreatePublisher(QueueOptions options)
            => new Publisher(CheckValue(options, nameof(options)), loggerFactory);

        /// <summary>
        /// Creates a queue message subscriber.
        /// </summary>
        public ISubscriber CreateSubscriber(QueueOptions options)
            => new Subscriber(CheckValue(options, nameof(options)), loggerFactory);
    }
}
