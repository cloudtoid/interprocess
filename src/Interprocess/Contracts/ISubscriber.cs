using System.Buffers;

namespace Cloudtoid.Interprocess;

/// <summary>
/// Message subscriber that subscribes to the messages published by the publisher.
/// </summary>
/// <remarks>
/// Disposal stops new reads, cancels blocking reads, and waits for admitted reads before releasing resources.
/// Calls rejected because the subscriber is disposed throw <see cref="OperationCanceledException"/>.
/// </remarks>
public interface ISubscriber : IDisposable
{
    /// <summary>
    /// Attempts to dequeue the next message if it is ready. This is a non-blocking
    /// call and returns immediately.
    /// This overload allocates a <see cref="byte"/> array the size of the message in the
    /// queue and copies the message from the shared memory to it. To avoid this memory
    /// allocation, consider reusing a previously allocated <see cref="byte"/> array with
    /// <see cref="TryDequeue(Memory{byte}, out ReadOnlyMemory{byte})"/>.
    /// <see cref="ArrayPool{T}"/> can be a good way of pooling and
    /// reusing byte arrays.
    /// </summary>
    /// <param name="message">The dequeued message.</param>
    /// <remarks>
    /// An unfinished reservation retains the read lock and its ten-second recovery deadline between attempts.
    /// Continue polling or dispose this subscriber; other subscribers may wait for that lock to expire.
    /// </remarks>
    /// <returns>Returns <see langword="false"/> if the queue is empty, a reader owns the lock,
    /// or the next message is not ready.</returns>
    bool TryDequeue(out ReadOnlyMemory<byte> message);

    /// <summary>
    /// Attempts to dequeue the next message if it is ready. This is a non-blocking
    /// call and returns immediately. This method populates the <paramref name="buffer"/> that is passed in.
    /// Make sure that the buffer is large enough to receive the entire message, or the message is truncated to fit the buffer.
    /// </summary>
    /// <param name="buffer">The memory buffer that is populated with the message. Make sure
    /// that the buffer is large enough to receive the entire message, or the message is
    /// truncated to fit the buffer.</param>
    /// <param name="message">The dequeued message.</param>
    /// <remarks>
    /// An unfinished reservation retains the read lock and its ten-second recovery deadline between attempts.
    /// Continue polling or dispose this subscriber; other subscribers may wait for that lock to expire.
    /// </remarks>
    /// <returns>Returns <see langword="false"/> if the queue is empty, a reader owns the lock,
    /// or the next message is not ready.</returns>
    bool TryDequeue(Memory<byte> buffer, out ReadOnlyMemory<byte> message);

    /// <summary>
    /// Dequeues a message from the queue. If the queue is empty, it *waits* for the
    /// arrival of a new message. This call is blocking until a message is received.
    /// This overload allocates a <see cref="byte"/> array the size of the message in the
    /// queue and copies the message from the shared memory to it. To avoid this memory
    /// allocation, consider reusing a previously allocated <see cref="byte"/> array with
    /// <see cref="Dequeue(Memory{byte}, CancellationToken)"/>.
    /// <see cref="ArrayPool{T}"/> can be a good way of pooling and
    /// reusing byte arrays.
    /// </summary>
    /// <param name="cancellation">A cancellation token to observe while waiting for the task to complete.</param>
    ReadOnlyMemory<byte> Dequeue(CancellationToken cancellation);

    /// <summary>
    /// Dequeues a message from the queue. If the queue is empty, it *waits* for the
    /// arrival of a new message. This call is blocking until a message is received.
    /// This method does not allocated memory and only populates
    /// the <paramref name="buffer"/> that is passed in. Make sure that the buffer is large
    /// enough to receive the entire message, or the message is truncated to fit the buffer.
    /// </summary>
    /// <param name="buffer">The memory buffer that is populated with the message. Make sure
    /// that the buffer is large enough to receive the entire message, or the message is
    /// truncated to fit the buffer.</param>
    /// <param name="cancellation">A cancellation token to observe while waiting for the task to complete.</param>
    ReadOnlyMemory<byte> Dequeue(
        Memory<byte> buffer,
        CancellationToken cancellation);
}