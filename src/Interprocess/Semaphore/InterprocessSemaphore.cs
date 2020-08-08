using System.IO;
using Cloudtoid.Interprocess.Semaphore.Unix;
using Cloudtoid.Interprocess.Semaphore.Windows;

namespace Cloudtoid.Interprocess
{
    /// <summary>
    /// This is a platform agnostic named semaphore. Named semaphores are synchronization
    /// constructs accessible across processes.
    /// </summary>
    internal static class InterprocessSemaphore
    {
        internal static IInterprocessSemaphoreWaiter CreateWaiter(SharedAssetsIdentifier identifier)
        {
            if (Util.IsUnixBased)
            {
                identifier = CreateUnixIdentifier(identifier);
                return new UnixSemaphoreWaiter(identifier);
            }

            return new WindowsSemaphore(identifier);
        }

        internal static IInterprocessSemaphoreReleaser CreateReleaser(SharedAssetsIdentifier identifier)
        {
            if (Util.IsUnixBased)
            {
                identifier = CreateUnixIdentifier(identifier);
                return new UnixSemaphoreReleaser(identifier);
            }

            return new WindowsSemaphore(identifier);
        }

        private static SharedAssetsIdentifier CreateUnixIdentifier(this SharedAssetsIdentifier identifier)
        {
            const string PathSuffix = ".cloudtoid/interprocess/sem";
            var path = Path.Combine(identifier.Path, PathSuffix);
            Directory.CreateDirectory(path);
            return new SharedAssetsIdentifier(identifier.Name, path);
        }
    }
}
