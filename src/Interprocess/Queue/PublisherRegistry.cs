using System.IO.MemoryMappedFiles;

namespace Cloudtoid.Interprocess;

// Retained by every queue participant, including subscribers. Reusable slots keep
// recovery proportional to concurrent publishers, not historical registrations.
internal sealed unsafe class PublisherRegistry : IDisposable
{
    private const int BlockSize = 4096;
    private const int SlotSize = 128;
    private readonly QueueOptions options;
    private readonly Block root;

    internal PublisherRegistry(QueueOptions options)
    {
        this.options = options;
        root = new Block(options, 0);
        Interlocked.CompareExchange(ref *(int*)root.Pointer, 1, 0);
    }

    internal int BlockCount => Volatile.Read(ref *(int*)root.Pointer);

    public void Dispose() => root.Dispose();

    internal PublisherLease Register(long id)
    {
        var lifetime = new ReaderLease(options, id);
        try
        {
            for (var index = 0; ; index++)
            {
                // Publish the block boundary before registering any writer in it.
                var count = BlockCount;
                if (index == count)
                    Interlocked.CompareExchange(ref *(int*)root.Pointer, checked(count + 1), count);

                var block = new Block(options, index);
                try
                {
                    for (var offset = SlotSize; offset < BlockSize; offset += SlotSize)
                    {
                        var slot = block.Pointer + offset;
                        var owner = Volatile.Read(ref *(long*)slot);
                        if (owner != 0 && ReaderLease.IsAlive(options, owner))
                            continue;

                        if (Interlocked.CompareExchange(ref *(long*)slot, id, owner) != owner)
                            continue;

                        // A dead owner's count can remain nonzero. No live call from that
                        // registration can resume, and a new call cannot start before return.
                        Interlocked.Exchange(ref *(int*)(slot + sizeof(long)), 0);
                        return new PublisherLease(lifetime, block, slot, id);
                    }
                }
                catch
                {
                    block.Dispose();
                    throw;
                }

                block.Dispose();
            }
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    // Admission must be closed first. Registrations created/reused after inspection
    // cannot start writing until the gate reopens. Unique IDs prevent slot-owner ABA.
    internal bool AnyActive()
    {
        var count = BlockCount;
        for (var index = 0; index < count; index++)
        {
            using var block = new Block(options, index);
            for (var offset = SlotSize; offset < BlockSize; offset += SlotSize)
            {
                var slot = block.Pointer + offset;
                var owner = Volatile.Read(ref *(long*)slot);
                if (owner != 0
                    && Volatile.Read(ref *(int*)(slot + sizeof(long))) != 0
                    && ReaderLease.IsAlive(options, owner))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static void Cleanup(QueueOptions options)
    {
        var directory = GetDirectory(options);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static string GetDirectory(QueueOptions options) =>
        Path.Combine(options.Path, ".cloudtoid/interprocess/v3/publishers", options.QueueName);

    internal sealed class Block : IDisposable
    {
        private readonly FileStream? file;
        private readonly MemoryMappedFile mapping;
        private readonly MemoryMappedViewAccessor view;

        internal Block(QueueOptions options, int index)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    mapping = MemoryMappedFile.CreateOrOpen(
                        "CT3_PUBLISHERS_" + options.QueueName + "." + index.ToStringInvariant(), BlockSize);
                }
                else
                {
                    var directory = GetDirectory(options);
                    Directory.CreateDirectory(directory);
                    file = new FileStream(
                        Path.Combine(directory, index.ToStringInvariant()),
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.ReadWrite | FileShare.Delete);
                    mapping = MemoryMappedFile.CreateFromFile(
                        file,
                        null,
                        BlockSize,
                        MemoryMappedFileAccess.ReadWrite,
                        HandleInheritability.None,
                        leaveOpen: true);
                }

                view = mapping.CreateViewAccessor();
                byte* pointer = null;
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                Pointer = pointer;
            }
            catch
            {
                view?.Dispose();
                mapping?.Dispose();
                file?.Dispose();
                throw;
            }
        }

        internal byte* Pointer { get; }

        public void Dispose()
        {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
            view.Dispose();
            mapping.Dispose();
            file?.Dispose();
        }
    }
}