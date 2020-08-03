using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    public interface ISubscriber : IDisposable
    {
        /// <summary>
        /// Dequeues a message from the queue if it is not empty.
        /// </summary>
        /// <returns>Returns <see langword="false"/> if the queue is empty.</returns>
        ValueTask<bool> TryDequeueAsync(
            CancellationToken cancellationToken,
            out ReadOnlyMemory<byte> message);

        /// <summary>
        /// Dequeues a message from the queue. If the queue is empty,
        /// it *waits* for the arrival of a new message.
        /// </summary>
        ValueTask<ReadOnlyMemory<byte>> DequeueAsync(
            CancellationToken cancellationToken);
    }
}
