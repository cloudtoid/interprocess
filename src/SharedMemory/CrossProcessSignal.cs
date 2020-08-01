using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cloudtoid.SharedMemory
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
            private readonly EventWaitHandle handle;

            internal CrossProcessWindowsSignal(string queueName)
            {
                handle = new EventWaitHandle(true, EventResetMode.AutoReset, "SMQ_" + queueName);
            }

            public override void Dispose() => handle.Dispose();
            internal override void Signal() => handle.Set();
            internal override void Wait(int millisecondsTimeout) => handle.WaitOne(millisecondsTimeout);
        }

        private sealed class CrossProcessUnixSignal : CrossProcessSignal
        {
            private readonly string filePath;
            private readonly FileSystemWatcher watcher;

            internal CrossProcessUnixSignal(string queueName, string path)
            {
                var fileName = queueName + ".fw";
                filePath = Path.Combine(path, fileName);

                if (!File.Exists(filePath))
                    File.WriteAllText(filePath, "sync file");

                watcher = new FileSystemWatcher(path, fileName);
            }

            public override void Dispose() => watcher.Dispose();
            internal override void Signal() => File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow);
            internal override void Wait(int millisecondsTimeout)
                => watcher.WaitForChanged(WatcherChangeTypes.Changed, millisecondsTimeout);
        }
    }
}   
