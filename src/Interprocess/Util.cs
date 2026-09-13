using System.Reflection;

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
}