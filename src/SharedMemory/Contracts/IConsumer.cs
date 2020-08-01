using System;
using System.Threading;

namespace Cloudtoid.SharedMemory
{
    public interface IConsumer
    {
        bool TryDequeue(CancellationToken cancellationToken, out ReadOnlyMemory<byte> message);
    }
}
