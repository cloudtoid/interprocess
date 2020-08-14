using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Threading;

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

            foreach (var file in Directory.EnumerateFiles(Identifier.Path))
                Util.TryDeleteFile(file);

            try
            {
                Directory.Delete(Identifier.Path);
            }
            catch
            {
                foreach (var file in Directory.EnumerateFiles(Identifier.Path))
                    logger.LogError("failed to delete file: " + file);

                throw;
            }
        }

        internal SharedAssetsIdentifier Identifier { get; }
    }
}
