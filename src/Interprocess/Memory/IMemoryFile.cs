using System.IO.MemoryMappedFiles;

namespace Cloudtoid.Interprocess;

internal interface IMemoryFile : IDisposable
{
    MemoryMappedFile MappedFile { get; }
}