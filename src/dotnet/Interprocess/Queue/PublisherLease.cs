namespace Cloudtoid.Interprocess;

// Local admission protects the mapping lifetime; the shared count protects writes
// from recovery in other processes. Only a proven-dead registration can be stolen.
internal sealed unsafe class PublisherLease(
    ReaderLease lifetime,
    byte* slot,
    long id) : IDisposable
{
    public void Dispose()
    {
        Interlocked.CompareExchange(ref *(long*)slot, 0, id);
        lifetime.Dispose();
    }

    internal void Enter() => Interlocked.Increment(ref *(int*)(slot + sizeof(long)));
    internal void Exit() => Interlocked.Decrement(ref *(int*)(slot + sizeof(long)));
}