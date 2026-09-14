namespace Cloudtoid.Interprocess;

// Fixed slots live in the queue mapping and share its lifetime.
internal sealed unsafe class PublisherRegistry(QueueOptions options, byte* queue)
{
    internal const int MaximumPublishers = 2048;
    internal const int SlotSize = 128;
    internal const int TableOffset = 128; // Keep counters apart from the 32-byte queue header.
    internal const int BufferOffset = TableOffset + (MaximumPublishers * SlotSize);

    internal PublisherLease Register(long id)
    {
        var lifetime = new ReaderLease(options, id);
        try
        {
            // Prefer empty slots. Inspect OS leases only when the table is full.
            for (var pass = 0; pass < 2; pass++)
            {
                for (var index = 0; index < MaximumPublishers; index++)
                {
                    var slot = queue + TableOffset + (index * SlotSize);
                    var owner = Volatile.Read(ref *(long*)slot);
                    if (owner != 0 && (pass == 0 || ReaderLease.IsAlive(options, owner)))
                        continue;

                    if (Interlocked.CompareExchange(ref *(long*)slot, id, owner) != owner)
                        continue;

                    // No old call can resume, and the new publisher cannot enter until return.
                    Interlocked.Exchange(ref *(int*)(slot + sizeof(long)), 0);
                    return new PublisherLease(lifetime, slot, id);
                }
            }

            throw new InvalidOperationException(
                $"The queue supports at most {MaximumPublishers} connected publishers.");
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    // Admission must be closed first. A publisher registered after its slot is
    // inspected cannot start writing until the gate reopens.
    internal bool AnyActive()
    {
        for (var index = 0; index < MaximumPublishers; index++)
        {
            var slot = queue + TableOffset + (index * SlotSize);
            var owner = Volatile.Read(ref *(long*)slot);
            if (owner != 0
                && Volatile.Read(ref *(int*)(slot + sizeof(long))) != 0
                && ReaderLease.IsAlive(options, owner))
            {
                return true;
            }
        }

        return false;
    }
}