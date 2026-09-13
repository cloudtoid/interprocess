namespace Cloudtoid.Interprocess;

/// <summary>
/// Message publisher that publishes messages to the subscribers.
/// </summary>
public interface IPublisher : IDisposable
{
    /// <summary>Enqueues the message to be published to the subscribers.</summary>
    /// <remarks>
    /// Disposal stops new enqueue calls and waits for admitted calls to finish before releasing resources.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The publisher has started disposing.</exception>
    bool TryEnqueue(ReadOnlySpan<byte> message);
}