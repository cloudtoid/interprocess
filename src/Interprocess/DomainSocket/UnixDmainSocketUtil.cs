using System;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Cloudtoid.Interprocess.DomainSocket
{
    internal static class UnixDomainSocketUtil
    {
        internal static Socket CreateUnixDomainSocket()
            => new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        internal static T SocketOperation<T>(
            Action<AsyncCallback> begin,
            Func<IAsyncResult, T> end,
            CancellationToken cancellationToken)
        {
            ExceptionDispatchInfo? exceptionInfo = null;
            T result = default;

            using (var handle = new ManualResetEventSlim(false))
            {
                begin(
                    new AsyncCallback(token =>
                    {
                        try
                        {
                            result = end(token);
                        }
                        catch (Exception ex)
                        {
                            exceptionInfo = ExceptionDispatchInfo.Capture(ex);
                        }
                        finally
                        {
                            handle.Set();
                        }
                    }));

                handle.Wait(cancellationToken);
            }

            exceptionInfo?.Throw();
            return result!;
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
