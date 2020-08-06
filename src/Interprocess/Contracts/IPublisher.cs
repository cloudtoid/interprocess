using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    public interface IPublisher : IDisposable
    {
        Task<bool> TryEnqueueAsync(
            ReadOnlySpan<byte> message,
            CancellationToken cancellation);
    }
}
