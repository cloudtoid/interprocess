using System.IO.MemoryMappedFiles;

namespace Cloudtoid.Interprocess.Memory.Unix;

internal sealed class MemoryFileUnix : IMemoryFile
{
    private const string Folder = ".cloudtoid/interprocess/mmf";
    private readonly string directory;
    private readonly string file;
    private readonly string queueName;
    private readonly FileStream stream;
    private readonly ILogger<MemoryFileUnix> logger;
    private int disposed;

    internal MemoryFileUnix(QueueOptions options, ILoggerFactory loggerFactory)
    {
        logger = loggerFactory.CreateLogger<MemoryFileUnix>();
        queueName = options.QueueName;
        directory = Path.Combine(options.Path, Folder);
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, queueName + ".qu");

        using var coordination = UnixFileLock.AcquireDirectory(directory);
        stream = new FileStream(
            file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            if (UnixFileLock.TryAcquireExclusive(stream.SafeFileHandle))
            {
                // No live participants: recover any resources left behind by a crash.
                InterprocessSemaphore.Unlink(queueName);
                stream.SetLength(0);
            }

            // Retain this lock until both the semaphore and memory view have closed.
            UnixFileLock.AcquireShared(stream.SafeFileHandle);
            MappedFile = MemoryMappedFile.CreateFromFile(
                stream,
                mapName: null,
                options.GetQueueStorageSize(),
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public MemoryMappedFile MappedFile { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        try
        {
            using var coordination = UnixFileLock.AcquireDirectory(directory);
            try
            {
                MappedFile.Dispose();
                if (UnixFileLock.TryAcquireExclusive(stream.SafeFileHandle))
                {
                    // Joining and leaving are serialized, so a new participant cannot open
                    // the old resources between this check and their removal.
                    InterprocessSemaphore.Unlink(queueName);
                    if (!PathUtil.TryDeleteFile(file))
                        logger.FailedToDeleteSharedMemoryFile();
                }
            }
            finally
            {
                stream.Dispose();
            }
        }
        finally
        {
            MappedFile.Dispose();
            stream.Dispose();
        }
    }
}