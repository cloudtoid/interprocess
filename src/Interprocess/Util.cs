using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    internal static class Util
    {
        internal static bool IsUnixBased { get; } =
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD);

        internal static Socket CreateUnixDomainSocket(bool blocking = true)
        {
            return new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            {
                Blocking = blocking
            };
        }

        internal static UnixDomainSocketEndPoint CreateUnixDomainSocketEndPoint(string file)
        {
            // the file path length limit is 104 on macOS. therefore, we try to
            // get the shorter path of full and relative paths.
            return new UnixDomainSocketEndPoint(ShortenPath(file));
        }

        internal static void SafeDispose(this Socket? socket)
        {
            try
            {
                socket?.Dispose();
            }
            catch
            {
                Console.WriteLine("Failed to dispose a socket.");
            }
        }

        internal static bool TryDeleteFile(string file)
        {
            try
            {
                File.Delete(file);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a unique file name in the <paramref name="path"/> ensuring that
        /// such a file name does not exist. The path returned is the shorter of
        /// absolute and relative paths to this new unique file.
        /// </summary>
        internal static string CreateShortUniqueFileName(
            string path,
            string fileName,
            string fileExtension)
        {
            path = ShortenPath(path);
            string filePath;
            do
            {
                var index = DateTime.Now.Ticks % 0xFFFFF;
                var name = fileName + index.ToString("X5", CultureInfo.InvariantCulture) + fileExtension;
                filePath = Path.Combine(path, name);
            }
            while (File.Exists(filePath));

            return filePath;
        }

        internal static string GetAbsolutePath(string path)
            => Path.IsPathRooted(path) ? path : Path.Combine(Environment.CurrentDirectory, path);

        /// <summary>
        /// Logs a critical error and then crashes the process
        /// </summary>
        internal static void FailFast<TCategoryName>(
            this ILogger<TCategoryName> logger,
            string message,
            Exception exception)
        {
            logger.LogCritical(exception, message);
            Environment.FailFast(message);
        }

        // shortens a file path by choosing the shorter of absolute and relative paths
        private static string ShortenPath(string path)
        {
            var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, path);
            return relativePath.Length < path.Length ? relativePath : path;
        }
    }
}
