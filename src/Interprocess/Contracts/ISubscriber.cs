using System;
using System.Threading;

namespace Cloudtoid.Interprocess
{
    public interface IConsumer
    {
        bool TryDequeue(CancellationToken cancellationToken, out ReadOnlyMemory<byte> message);
    }
}
