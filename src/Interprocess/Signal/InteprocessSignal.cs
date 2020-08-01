using System;
using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    internal abstract partial class InteprocessSignal : IDisposable
    {
        public abstract void Dispose();
        internal abstract void Signal();
        internal abstract void Wait(int millisecondsTimeout);

        internal static InteprocessSignal Create(string queueName, string path)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsSignal(queueName)
                : (InteprocessSignal)new LinuxSignal(queueName, path);
        }
    }
}   
