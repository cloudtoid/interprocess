using System.Net.Sockets;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal static class UnixDomainSocketUtil
    {
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
            return new UnixDomainSocketEndPoint(PathUtil.ShortenPath(file));
        }
    }
}
