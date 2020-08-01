using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace Cloudtoid.SharedMemory
{
    // This class managed the underlying Memory Mapped File and View
    internal class SharedMemoryView : IDisposable
    {
        private readonly QueueOptions options;
        private const string MemoryMappedFileExtension = ".qu";
        private readonly MemoryMappedFile file;
        private readonly MemoryMappedViewAccessor view;

        internal unsafe SharedMemoryView(QueueOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));

            try
            {
                file = CreateMemoryMappedFile();
                view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
                Pointer = AcquirePointer();
            }
            catch
            {
                if (Pointer != null)
                    view?.SafeMemoryMappedViewHandle.ReleasePointer();

                view?.Dispose();
                file?.Dispose();
                DeleteMemoryMappedFileIfNeeded();

                throw;
            }
        }

        public unsafe byte* Pointer { get; }

        ~SharedMemoryView()
            => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
                view.Dispose();
                file.Dispose();
            }

            DeleteMemoryMappedFileIfNeeded();
        }

        private unsafe MemoryMappedFile CreateMemoryMappedFile()
        {
            var filePath = GetMemoryMappedFilePath();
            var capacity = options.Capacity + sizeof(QueueHeader);

            var stream = new FileStream(
                filePath,
                options.CreateOrOverride ? FileMode.Create : FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                bufferSize: 0x1000,
                FileOptions.None);

            try
            {
                return MemoryMappedFile.CreateFromFile(
                    stream,
                    mapName: null, // do not set this or it will not work on Linux/Unix/MacOS 
                    capacity,
                    MemoryMappedFileAccess.ReadWrite,
                    HandleInheritability.None,
                    false);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private void DeleteMemoryMappedFileIfNeeded()
        {
            if (!options.CreateOrOverride)
                return;

            var path = GetMemoryMappedFilePath();

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                Console.WriteLine("Failed to delete queue's shared memory backing file.");
            }
        }

        private string GetMemoryMappedFilePath()
            => Path.Combine(options.Path, options.QueueName + MemoryMappedFileExtension);

        private unsafe byte* AcquirePointer()
        {
            byte* ptr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            if (ptr == null)
                throw new InvalidOperationException("Failed to acquire a pointer to the memory mapped file view.");

            return ptr;
        }
    }
}
