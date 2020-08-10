using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Cloudtoid.Interprocess.Memory.Unix;
using Cloudtoid.Interprocess.Memory.Windows;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    // This class manages the underlying Memory Mapped File
    internal class MemoryView : IDisposable
    {
        private readonly IMemoryFile file;
        private readonly MemoryMappedViewAccessor view;

        internal unsafe MemoryView(QueueOptions options, ILogger logger)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            file = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsMemoryFile(options)
                : (IMemoryFile)new UnixMemoryFile(options, logger);

            try
            {
                view = file.MappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
                Pointer = AcquirePointer();
            }
            catch
            {
                if (Pointer != null)
                    view?.SafeMemoryMappedViewHandle.ReleasePointer();

                view?.Dispose();
                file.Dispose();

                throw;
            }
        }

        public unsafe byte* Pointer { get; }

        public void Dispose()
        {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
            view.Flush();
            view.Dispose();
            file.Dispose();
        }

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
