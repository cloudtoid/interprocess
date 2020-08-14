using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;

namespace Cloudtoid.Interprocess.Tests
{
    public class UniqueIdentifierFixture : IDisposable
    {
        private static readonly string Root = Path.GetTempPath();

        public UniqueIdentifierFixture()
        {
            while (true)
            {
                var folder = (DateTime.UtcNow.Ticks % 0xFFFFF).ToString("X5", CultureInfo.InvariantCulture);
                var path = Path.Combine(Root, folder);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    Identifier = new SharedAssetsIdentifier("qn", path);
                    break;
                }
            }
        }

        public void Dispose()
        {
            var logger = TestUtils.LoggerFactory.CreateLogger("TEST");

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = false,
                AttributesToSkip = 0
            };

            foreach (var file in Directory.EnumerateFiles(Identifier.Path, "*", options))
            {
                logger.LogInformation($"Deleting file: {file}");
                Util.TryDeleteFile(file);
            }

            logger.LogInformation($"Deleting dir: {Identifier.Path}");
            try
            {
                Directory.Delete(Identifier.Path);
            }
            catch (Exception ex)
            {
                foreach (var file in Directory.EnumerateFiles(Identifier.Path))
                    logger.LogError($"New file? {file}");

                logger.LogError(ex, "Failed to delete " + Identifier.Path);
            }
        }

        internal SharedAssetsIdentifier Identifier { get; }
    }
}
