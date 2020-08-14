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
            foreach (var file in Directory.EnumerateFiles(Identifier.Path))
                Util.TryDeleteFile(file);

            try
            {
                Directory.Delete(Identifier.Path);
            }
            catch { }
        }

        internal SharedAssetsIdentifier Identifier { get; }
    }
}
