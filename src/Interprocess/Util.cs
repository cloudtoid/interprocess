using System;
using System.IO;
using System.Net.Sockets;

namespace Cloudtoid.Interprocess
{
    internal static class Util
    {
        internal static Socket CreateUnixDomainSocket(bool blocking = true)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Blocking = blocking;
            return socket;
        }

        internal static void SafeDispose(this Socket? socket)
        {
            try
            {
                socket?.Dispose();
            }
            catch
            {
                Console.WriteLine("Failed to dispose a socket");
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
                Console.WriteLine("Failed to dispose a socket");
                return false;
            }
        }

        internal static string GetAbsolutePath(string path)
            => Path.IsPathRooted(path) ? path : Path.Combine(Environment.CurrentDirectory, path);
    }
}
