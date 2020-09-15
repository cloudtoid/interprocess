using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
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

            try
            {
                // assume that the file doesn't exist and try and create it

                stream = new FileStream(
                    filePath,
                    FileMode.CreateNew,
                    FileAccessOption,
                    FileShareOption,
                    BufferSize,
                    FileOptions.DeleteOnClose);

                mustDeleteFileOnDispose = true;
            }
            catch (IOException)
            {
                if (options.CreateOrOverride)
                {
                    stream = new FileStream(
                        filePath,
                        FileMode.Create, // create or override
                        FileAccessOption,
                        FileShareOption,
                        BufferSize,
                        FileOptions.DeleteOnClose);

                    mustDeleteFileOnDispose = true;
                }
                else
                {
                    try
                    {
                        stream = new FileStream(
                            filePath,
                            FileMode.Open,
                            FileAccessOption,
                            FileShareOption,
                            BufferSize,
                            FileOptions.None);
                    }
                    catch (FileNotFoundException)
                    {
                        // wait a bit in case there is a race condition and the file
                        // is just being created
                        Thread.Sleep(1000);

                        stream = new FileStream(
                            filePath,
                            FileMode.Open,
                            FileAccessOption,
                            FileShareOption,
                            BufferSize,
                            FileOptions.None);
                    }
                }
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
    }
}
