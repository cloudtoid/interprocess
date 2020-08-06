using System;
using System.Net.Sockets;
using System.Threading;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal static class UnixDomainSocketUtil
    {
        internal static Socket CreateUnixDomainSocket()
            => new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        internal static T SocketOperation<T>(
            Func<AsyncCallback, IAsyncResult> begin,
            Func<IAsyncResult, T> end,
            CancellationToken cancellationToken)
        {
            using var waitHandle = new ManualResetEventSlim(false);

            var token = begin(_ => waitHandle.Set());

            if(!token.IsCompleted)
                waitHandle.Wait(cancellationToken);

            return end(token);
        }

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
