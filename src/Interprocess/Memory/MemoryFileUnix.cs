using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess.Memory.Unix
{
    internal sealed class MemoryFileUnix : IMemoryFile
    {
        private const FileAccess FileAccessOption = FileAccess.ReadWrite;
        private const FileShare FileShareOption = FileShare.ReadWrite | FileShare.Delete;
        private const string Folder = ".cloudtoid/interprocess/mmf";
        private const string FileExtension = ".qu";
        private const int BufferSize = 0x1000;
        private readonly string filePath;
        private readonly bool mustDeleteFileOnDispose;
        private readonly ILogger<MemoryFileUnix> logger;

        internal MemoryFileUnix(QueueOptions options, ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger<MemoryFileUnix>();
            filePath = Path.Combine(options.Path, Folder);
            Directory.CreateDirectory(filePath);
            filePath = Path.Combine(filePath, options.QueueName + FileExtension);

            FileStream stream;

            if (IsFileInUse(filePath))
            {
                // just open the file

                stream = new FileStream(
                    filePath,
                    FileMode.Open, // just open it
                    FileAccessOption,
                    FileShareOption,
                    BufferSize,
                    FileOptions.None);
            }
            else
            {
                // override (or create if no longer exist) as it is not being used

                stream = new FileStream(
                    filePath,
                    FileMode.Create,
                    FileAccessOption,
                    FileShareOption,
                    BufferSize,
                    FileOptions.DeleteOnClose);

                mustDeleteFileOnDispose = true;
            }

            try
            {
                MappedFile = MemoryMappedFile.CreateFromFile(
                    stream,
                    mapName: null, // do not set this or it will not work on Linux/Unix/MacOS
                    options.BytesCapacity,
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

        ~MemoryFileUnix()
           => Dispose(false);

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

        /// <summary>
        /// Deletes the backing file if it was created by this instance of <see cref="MemoryFileUnix"/>.
        /// </summary>
        private void ResetBackingFile()
        {
            if (mustDeleteFileOnDispose && !PathUtil.TryDeleteFile(filePath))
                logger.LogError("Failed to delete queue's shared memory backing file.");
        }

        private static bool IsFileInUse(string file)
        {
            try
            {
                using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
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
}
