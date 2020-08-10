using System.IO;
using Cloudtoid.Interprocess.Semaphore;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    /// <summary>
    /// This class mimics the behavior of a platform agnostic named semaphore.
    /// Named semaphores are synchronization constructs accessible across processes.
    /// </summary>
    /// <remarks>
    /// Named semaphores are pretty slow. Therefore, to replicate a named semaphore
    /// in the most efficient possible way, we are using Unix Domain Sockets to send
    /// signals between processes.
    /// 
    /// It is worth mentioning that we support multiple signal publishers and
    /// receivers; therefore, you will find some logic on Unix to utilize multiple
    /// named sockets. We also use a file system watcher to keep track of the
    /// addition and removal of signal publishers (Unix Domain Sockets use backing
    /// files).
    /// </remarks>
    internal static class InterprocessSemaphore
    {
        internal static IInterprocessSemaphoreWaiter CreateWaiter(
            SharedAssetsIdentifier identifier,
            ILogger logger)
        {
            identifier = CreateUnixDomainSocketIdentifier(identifier);
            return new SemaphoreWaiter(identifier, logger);
        }

        internal static IInterprocessSemaphoreReleaser CreateReleaser(
            SharedAssetsIdentifier identifier,
            ILogger logger)
        {
            identifier = CreateUnixDomainSocketIdentifier(identifier);
            return new SemaphoreReleaser(identifier, logger);
        }

        private static SharedAssetsIdentifier CreateUnixDomainSocketIdentifier(this SharedAssetsIdentifier identifier)
        {
            var path = Path.Combine(identifier.Path, Constants.UnixDomainSocketFilePathSuffix);
            Directory.CreateDirectory(path);
            return new SharedAssetsIdentifier(identifier.Name, path);
        }
    }
}
