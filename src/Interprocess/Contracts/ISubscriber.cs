using System;
using System.Threading;

namespace Cloudtoid.Interprocess
{
    public interface ISubscriber : IDisposable
    {
        bool TryDequeue(CancellationToken cancellationToken, out ReadOnlyMemory<byte> message);
    }
}
