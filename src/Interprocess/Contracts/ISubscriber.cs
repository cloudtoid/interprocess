using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    public interface ISubscriber : IDisposable
    {
        /// <summary>
        /// Dequeues a message from the queue if the queue is not empty. This is a non-blocking
        /// call and returns immediately.
        /// This overload allocates a <see cref="byte[]"/> the size of the message in the
        /// queue and copies the message from the shared memory to it. To avoid this memory
        /// allocation, consider reusing a previously allocated <see cref="byte[]"/> with
        /// <see cref="TryDequeueAsync(Memory{byte}?, CancellationToken, out ReadOnlyMemory{byte})"/>.
        /// <see cref="System.Buffers.ArrayPool{byte}"/> can be a good way of pooling and
        /// reusing byte arrays.
        /// </summary>
        /// <returns>Returns <see langword="false"/> if the queue is empty.</returns>
        ValueTask<bool> TryDequeueAsync(
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message);

        /// <summary>
        /// Dequeues a message from the queue if the queue is not empty. This is a non-blocking
        /// call and returns immediately. This method does not allocated memory and only populates
        /// the <paramref name="buffer"/> that is passed in. Make sure that the buffer is large
        /// enough to receive the entire message, or the message is truncated to fit the buffer.
        /// </summary>
        /// <param name="buffer">The memory buffer that is populated with the message. Make sure
        /// that the buffer is large enough to receive the entire message, or the message is
        /// truncated to fit the buffer.</param>
        /// <returns>Returns <see langword="false"/> if the queue is empty.</returns>
        ValueTask<bool> TryDequeueAsync(
            Memory<byte> buffer,
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message);

        /// <summary>
        /// Dequeues a message from the queue. If the queue is empty, it *waits* for the
        /// arrival of a new message. This call is blocking until a message is received.
        /// This overload allocates a <see cref="byte[]"/> the size of the message in the
        /// queue and copies the message from the shared memory to it. To avoid this memory
        /// allocation, consider reusing a previously allocated <see cref="byte[]"/> with
        /// <see cref="DequeueAsync(Memory{byte}?, CancellationToken)"/>.
        /// <see cref="System.Buffers.ArrayPool{byte}"/> can be a good way of pooling and
        /// reusing byte arrays.
        /// </summary>
        ValueTask<ReadOnlyMemory<byte>> DequeueAsync(
            CancellationToken cancellation);

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
        ValueTask<ReadOnlyMemory<byte>> DequeueAsync(
            Memory<byte> buffer,
            CancellationToken cancellation);
    }
}
