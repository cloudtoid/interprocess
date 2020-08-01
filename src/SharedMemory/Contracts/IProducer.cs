using System;

namespace Cloudtoid.SharedMemory
{
    public interface IProducer
    {
        bool TryEnqueue(ReadOnlySpan<byte> message);
    }
}
