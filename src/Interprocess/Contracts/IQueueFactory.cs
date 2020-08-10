namespace Cloudtoid.Interprocess
{
    public interface IQueueFactory
    {
        IPublisher CreatePublisher(QueueOptions options);
        ISubscriber CreateSubscriber(QueueOptions options);
    }
}
