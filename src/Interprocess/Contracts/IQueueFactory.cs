namespace Cloudtoid.Interprocess;

/// <summary>Factory to create queue publishers and subscribers. </summary>
public interface IQueueFactory
{
    /// <summary> Creates a queue message publisher. </summary>
    IPublisher CreatePublisher(QueueOptions options);

    /// <summary> Creates a queue message subscriber.</summary>
    ISubscriber CreateSubscriber(QueueOptions options);
}