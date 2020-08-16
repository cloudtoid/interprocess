using System;
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
                var folder = (DateTime.UtcNow.Ticks % 0xFFFFF).ToStringInvariant("X5");
                var path = Path.Combine(Root, folder);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    Identifier = new SharedAssetsIdentifier("qn", path);
                    break;
                }
            }
        }

        internal SharedAssetsIdentifier Identifier { get; }

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
    }
}
