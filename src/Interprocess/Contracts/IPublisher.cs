using System;

namespace Cloudtoid.Interprocess
{
    public interface IPublisher : IDisposable
    {
        bool TryEnqueue(ReadOnlySpan<byte> message);
    }
}
