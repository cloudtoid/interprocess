using System;
using System.Threading;

namespace Cloudtoid.Interprocess
{
    public interface IPublisher : IDisposable
    {
        bool TryEnqueue(
            ReadOnlySpan<byte> message,
            CancellationToken cancellation);
    }
}
