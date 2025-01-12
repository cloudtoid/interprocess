using System.IO.MemoryMappedFiles;

namespace Cloudtoid.Interprocess.Memory.Unix;

internal sealed class MemoryFileUnix : IMemoryFile
{
    private const FileAccess FileAccessOption = FileAccess.ReadWrite;
    private const FileShare FileShareOption = FileShare.ReadWrite | FileShare.Delete;
    private const string Folder = ".cloudtoid/interprocess/mmf";
    private const string FileExtension = ".qu";
    private const int BufferSize = 0x1000;
    private readonly string file;
    private readonly ILogger<MemoryFileUnix> logger;

    internal MemoryFileUnix(QueueOptions options, ILoggerFactory loggerFactory)
    {
        logger = loggerFactory.CreateLogger<MemoryFileUnix>();
        file = Path.Combine(options.Path, Folder);
        Directory.CreateDirectory(file);
        file = Path.Combine(file, options.QueueName + FileExtension);

        FileStream stream;

        if (IsFileInUse(file))
        {
            // just open the file

#pragma warning disable CA2000
            stream = new FileStream(
                file,
                FileMode.Open, // just open it
                FileAccessOption,
                FileShareOption,
                BufferSize);
        }
        else
        {
            // override (or create if no longer exist) as it is not being used

            stream = new FileStream(
                file,
                FileMode.Create,
                FileAccessOption,
                FileShareOption,
                BufferSize);
#pragma warning restore CA2000
        }

        try
        {
            MappedFile = MemoryMappedFile.CreateFromFile(
                stream,
                mapName: null, // do not set this or it will not work on Linux/Unix/MacOS
                options.GetQueueStorageSize(),
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                false);
        }
        catch
        {
            // do not leave any resources hanging

            try
            {
                stream.Dispose();
            }
            catch
            {
                ResetBackingFile();
            }

            throw;
        }
    }

    ~MemoryFileUnix() =>
       Dispose(false);

    public MemoryMappedFile MappedFile { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
                MappedFile.Dispose();
        }
        finally
        {
            ResetBackingFile();
        }
    }

    private void ResetBackingFile()
    {
        // Deletes the backing file if it is not used by any other process

        if (IsFileInUse(file))
            return;

        if (!PathUtil.TryDeleteFile(file))
            logger.FailedToDeleteSharedMemoryFile();
    }

    private static bool IsFileInUse(string file)
    {
        try
        {
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            {
            }

            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}