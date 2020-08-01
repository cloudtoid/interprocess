namespace Cloudtoid.Interprocess
{
    public static class InterprocessQueue
    {
        public static IPublisher CreatePublisher(QueueOptions options)
            => new Publisher(options);

        public static ISubscriber CreateSubscriber(QueueOptions options)
            => new Subscriber(options);
    }
}
