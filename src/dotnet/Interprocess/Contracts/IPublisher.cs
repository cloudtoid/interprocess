namespace Cloudtoid.Interprocess;

/// <summary>
/// Message publisher that publishes messages to the subscribers.
/// </summary>
public interface IPublisher : IDisposable
{
    /// <summary>Enqueues the message to be published to the subscribers.</summary>
    /// <remarks>
    /// Disposal stops new enqueue calls and waits for admitted calls to finish before releasing resources.
    /// A full notification semaphore does not fail an already committed message.
    /// Positions never wrap. After counter exhaustion, drain the queue and move all participants to a fresh queue.
    /// </remarks>
    /// <returns>False when the message does not fit or recovery temporarily closes admission; otherwise true.</returns>
    /// <exception cref="ObjectDisposedException">The publisher has started disposing.</exception>
    /// <exception cref="OverflowException">The reservation would exceed the queue's lifetime byte limit.</exception>
    bool TryEnqueue(ReadOnlySpan<byte> message);
}