using System;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    internal static class Util
    {
        internal static bool IsUnixBased { get; } =
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD);

        internal static void Ensure64Bit()
        {
            if (Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
                return;

            throw new NotSupportedException(
                $"{Assembly.GetExecutingAssembly().GetName().Name} only supports 64-bit processor architectures.");
        }

        internal static void SafeDispose(this Socket? socket, ILogger? logger = null)
        {
            try
            {
                socket?.Dispose();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to dispose a socket.");
            }
        }

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

        internal static void LoopTillCancelled(
            Action<CancellationToken> action,
            ILogger logger,
            CancellationToken cancellation)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        action(cancellation);
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested && !ex.IsFatal())
                    {
                        logger.LogError(
                            ex,
                            $"Received an unexpected error in a safe loop while the cancellation token is still active. " +
                            $"We will ignore this exception and continue with the loop.");
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested) { }
        }
    }
}
