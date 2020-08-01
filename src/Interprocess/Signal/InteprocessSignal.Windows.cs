using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cloudtoid.Interprocess
{
    internal abstract class CrossProcessSignal : IDisposable
    {
        public abstract void Dispose();
        internal abstract void Signal();
        internal abstract void Wait(int millisecondsTimeout);

        internal static CrossProcessSignal Create(string queueName, string path)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new CrossProcessWindowsSignal(queueName)
                : (CrossProcessSignal)new CrossProcessUnixSignal(queueName, path);
        }

        private sealed class CrossProcessWindowsSignal : CrossProcessSignal
        {
            private const string HandleNamePrefix = "SMQ_";
            private readonly EventWaitHandle handle;

            internal CrossProcessWindowsSignal(string queueName)
            {
                handle = new EventWaitHandle(true, EventResetMode.AutoReset, HandleNamePrefix + queueName);
            }

            public override void Dispose()
                => handle.Dispose();

            internal override void Signal()
                => handle.Set();

            internal override void Wait(int millisecondsTimeout)
                => handle.WaitOne(millisecondsTimeout);
        }

        private sealed class CrossProcessUnixSignal : CrossProcessSignal
        {
            private const string FileExtension = ".fw";
            private readonly string filePath;
            private readonly AutoResetEvent handle;
            private readonly FileSystemWatcher watcher;

            internal CrossProcessUnixSignal(string queueName, string path)
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
