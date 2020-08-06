using System.Net.Sockets;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal static class UnixDomainSocketUtil
    {
        internal static Socket CreateUnixDomainSocket()
            => new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        internal static void SafeDispose(this Socket? socket)
        {
            try
            {
                socket?.Dispose();
            }
            catch { }
        }
    }
}
