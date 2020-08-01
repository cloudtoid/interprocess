using System;
using System.IO;
using System.Threading;

namespace Cloudtoid.Interprocess
{
    internal partial class InteprocessSignal
    {
        private sealed class LinuxSignal : InteprocessSignal
        {
            private const string FileExtension = ".fw";
            private readonly string filePath;
            private readonly AutoResetEvent handle;
            private readonly FileSystemWatcher watcher;

            internal LinuxSignal(string queueName, string path)
            {
                var fileName = queueName + FileExtension;
                filePath = Path.Combine(path, fileName);

                if (!File.Exists(filePath))
                    File.WriteAllText(filePath, "shared memory queue sync file");

                handle = new AutoResetEvent(true);

                watcher = new FileSystemWatcher(path, fileName);
                watcher.NotifyFilter = NotifyFilters.LastAccess;
                watcher.Changed += OnChanged;
                watcher.EnableRaisingEvents = true;
            }

            public override void Dispose()
            {
                watcher.Dispose();
                handle.Dispose();
            }

            internal override void Signal()
                => File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow);

            internal override void Wait(int millisecondsTimeout)
                => handle.WaitOne(millisecondsTimeout);

            private void OnChanged(object _, FileSystemEventArgs e)
                => handle.Set();
        }
    }
}
