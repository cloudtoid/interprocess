using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    public interface IPublisher : IDisposable
    {
        ValueTask<bool> TryEnqueueAsync(
            ReadOnlySpan<byte> message,
            CancellationToken cancellation);
    }
}
