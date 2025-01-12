using System.Reflection;
using System.Runtime.CompilerServices;

namespace Cloudtoid.Interprocess;

internal static class Util
{
    internal static void Ensure64Bit()
    {
        if (Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
            return;

        throw new NotSupportedException(
            $"{Assembly.GetExecutingAssembly().GetName().Name} only supports 64-bit processor architectures.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ThrowIfCancellationRequested(
        this CancellationTokenSource source,
        CancellationToken token = default)
    {
        // NOTE: The source could have been disposed. We can still access the IsCancellationRequested
        // property BUT we cannot access its Token property. Do NOT change this code.
        if (source.IsCancellationRequested)
            throw new OperationCanceledException();

        token.ThrowIfCancellationRequested();
    }
}