using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using Cloudtoid.Interprocess.Memory.Unix;

namespace Cloudtoid.Interprocess;

// Registered before a reader can own the queue lock, and retained until all its reads have stopped.
internal sealed class ReaderLease : IDisposable
{
    private readonly FileStream? file;
    private readonly MemoryMappedFile? mapping;
    private readonly string name;

    internal ReaderLease(QueueOptions options, long id)
    {
        name = GetName(options, id);
        if (OperatingSystem.IsWindows())
        {
            mapping = MemoryMappedFile.CreateNew(name, 16);
            try
            {
                using var process = Process.GetCurrentProcess();
                using var view = mapping.CreateViewAccessor();
                view.Write(0, process.Id);
                view.Write(8, process.StartTime.ToUniversalTime().Ticks);
            }
            catch
            {
                mapping.Dispose();
                throw;
            }
        }
        else
        {
            Directory.CreateDirectory(GetDirectory(options));
            file = new FileStream(
                name, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            try
            {
                if (!UnixFileLock.TryAcquireExclusive(file.SafeFileHandle))
                    throw new IOException("The reader registration is already in use.");
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }
    }

    public void Dispose()
    {
        mapping?.Dispose();
        if (file is not null)
        {
            file.Dispose();
            PathUtil.TryDeleteFile(name);
        }
    }

    internal static bool IsAlive(QueueOptions options, long id)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return IsWindowsOwnerAlive(GetName(options, id));

            var path = GetName(options, id);
            using var probe = new FileStream(
                path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            if (!UnixFileLock.TryAcquireExclusive(probe.SafeFileHandle))
                return true;

            // The owner closed its registration or died. IDs are never reused in a live queue.
            File.Delete(path);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            // Failure to inspect an owner is not evidence of its death.
            return true;
        }
    }

    internal static void Cleanup(QueueOptions options)
    {
        var directory = GetDirectory(options);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static string GetDirectory(QueueOptions options) =>
        Path.Combine(options.Path, ".cloudtoid/interprocess/v3/readers", options.QueueName);

    private static string GetName(QueueOptions options, long id) => OperatingSystem.IsWindows()
        ? "CT3_READER_" + options.QueueName + "." + id.ToStringInvariant()
        : Path.Combine(GetDirectory(options), id.ToStringInvariant());

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsOwnerAlive(string name)
    {
        using var mapping = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
        using var view = mapping.CreateViewAccessor(0, 16, MemoryMappedFileAccess.Read);
        var processId = view.ReadInt32(0);
        var started = view.ReadInt64(8);
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            // The process may exit between lookup, HasExited, and reading its start time.
            return false;
        }
    }
}