using System;
using System.IO;
using System.Threading;

namespace Cloudtoid.Interprocess
{
    internal partial class InteprocessSignal
    {
        // internal for testing
        internal sealed class UnixSignal : InteprocessSignal
        {
            private const string Folder = ".cloudtoid/interprocess/signal";
            private const string FileExtension = ".fw";
            private readonly string filePath;
            private readonly AutoResetEvent handle;
            private readonly FileSystemWatcher watcher;

            internal UnixSignal(string queueName, string path)
            {
                filePath = Path.Combine(path, Folder);
                Directory.CreateDirectory(filePath);

                var fileName = queueName + FileExtension;
                filePath = Path.Combine(path, fileName);

                if (!File.Exists(filePath))
                    File.WriteAllText(filePath, "interprocess sync file");

                handle = new AutoResetEvent(true);

                watcher = new FileSystemWatcher(path, fileName);
                watcher.NotifyFilter = NotifyFilters.LastAccess;
                watcher.Changed += OnChanged;
                watcher.EnableRaisingEvents = true;
            }

            public override void Dispose()
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                handle.Dispose();
            }

            internal override void Signal()
            {
                File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow);
                handle.Set();
            }

            internal override bool Wait(int millisecondsTimeout)
                => handle.WaitOne(millisecondsTimeout);

            private void OnChanged(object _, FileSystemEventArgs e)
                => handle.Set();
        }
    }
}
