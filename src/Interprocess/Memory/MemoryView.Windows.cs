using System.IO;
using System.IO.MemoryMappedFiles;

namespace Cloudtoid.Interprocess
{
    internal partial class MemoryView
    {
        private sealed class WindowsMemoryFile : IMemoryFile
        {
            private const string MapNamePrefix = "CT_IP_";

            internal WindowsMemoryFile(QueueOptions options)
            {
                MappedFile = MemoryMappedFile.CreateOrOpen(
                    mapName: MapNamePrefix + options.QueueName,
                    options.Capacity,
                    MemoryMappedFileAccess.ReadWrite,
                    MemoryMappedFileOptions.None,
                    HandleInheritability.None);
            }

            public MemoryMappedFile MappedFile { get; }

            public void Dispose()
                => MappedFile.Dispose();
        }
    }
}
