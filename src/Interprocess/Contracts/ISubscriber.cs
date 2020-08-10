using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    public interface ISubscriber : IDisposable
    {
        /// <summary>
        /// Dequeues a message from the queue if it is not empty.
        /// This is a non-blocking call and immediately returns.
        /// </summary>
        /// <returns>Returns <see langword="false"/> if the queue is empty.</returns>
        ValueTask<bool> TryDequeueAsync(
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message);

        /// <summary>
        /// Dequeues a message from the queue. If the queue is empty, it *waits* for the
        /// arrival of a new message. This is a blocking call until a message is received.
        /// </summary>
        ValueTask<ReadOnlyMemory<byte>> DequeueAsync(
            CancellationToken cancellation);
    }
}
