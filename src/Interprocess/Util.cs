using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    internal static class Util
    {
        internal static void Ensure64Bit()
        {
            if (Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
                return;

            throw new NotSupportedException(
                $"{Assembly.GetExecutingAssembly().GetName().Name} only supports 64-bit processor architectures.");
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowIfCancellationRequested(
            this CancellationTokenSource source,
            CancellationToken token = default)
        {
            // NOTE: The source could have been Disposed. We can still access the IsCancellationRequested
            // property BUT we cannot access its Token property. Do NOT change this code.
            if (source.IsCancellationRequested)
                throw new OperationCanceledException();

            token.ThrowIfCancellationRequested();
        }
    }
}
