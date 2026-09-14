using System.IO.MemoryMappedFiles;

namespace Cloudtoid.Interprocess.Memory.Windows;

internal sealed class MemoryFileWindows : IMemoryFile
{
    private const string MapNamePrefix = "CT3_IP_";

    internal MemoryFileWindows(QueueOptions options)
    {
#if NET5_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
#endif
        // Serialize opening and capacity initialization, never message delivery.
        // If the first process exits here, its uninitialized mapping loses its last
        // handle before another participant can open it.
        using var coordination = new Mutex(false, "CT3_INIT_" + options.QueueName);
        try
        {
            coordination.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // WaitOne acquired the mutex abandoned by the previous initializer.
        }

        try
        {
            MappedFile = MemoryMappedFile.CreateOrOpen(
                mapName: MapNamePrefix + options.QueueName,
                options.GetQueueStorageSize(),
                MemoryMappedFileAccess.ReadWrite,
                MemoryMappedFileOptions.None,
                HandleInheritability.None);
            try
            {
                // The alignment gap before the publisher table holds the logical capacity.
                using var view = MappedFile.CreateViewAccessor(0, 40);
                var capacity = view.ReadInt64(32);
                if (capacity == 0)
                    view.Write(32, options.Capacity);
                else if (capacity != options.Capacity)
                    throw new ArgumentException("The capacity must match the existing queue.", nameof(options));
            }
            catch
            {
                MappedFile.Dispose();
                throw;
            }
        }
        finally
        {
            coordination.ReleaseMutex();
        }
    }

    public MemoryMappedFile MappedFile { get; }

    public void Dispose() =>
        MappedFile.Dispose();
}