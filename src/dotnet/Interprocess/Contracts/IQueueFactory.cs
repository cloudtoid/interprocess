namespace Cloudtoid.Interprocess;

/// <summary>Factory to create queue publishers and subscribers. </summary>
public interface IQueueFactory
{
    /// <summary> Creates a queue message publisher. </summary>
    /// <exception cref="InvalidOperationException">The queue already has 2,048 connected publishers.</exception>
    IPublisher CreatePublisher(QueueOptions options);

    /// <summary> Creates a queue message subscriber.</summary>
    ISubscriber CreateSubscriber(QueueOptions options);
}