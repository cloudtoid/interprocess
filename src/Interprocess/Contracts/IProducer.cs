using System;

namespace Cloudtoid.Interprocess
{
    public interface IProducer
    {
        bool TryEnqueue(ReadOnlySpan<byte> message);
    }
}
