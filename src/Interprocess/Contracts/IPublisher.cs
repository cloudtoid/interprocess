namespace Cloudtoid.Interprocess;

/// <summary>
/// Message publisher that publishes messages to the subscribers.
/// </summary>
public interface IPublisher : IDisposable
{
    /// <summary>Enqueues the message to be published to the subscribers.</summary>
    bool TryEnqueue(ReadOnlySpan<byte> message);
}