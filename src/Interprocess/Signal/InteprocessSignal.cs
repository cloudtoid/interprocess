using System;
using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    internal abstract partial class InteprocessSignal : IDisposable
    {
        public abstract void Dispose();
        internal abstract void Signal();
        internal abstract bool Wait(int millisecondsTimeout);

        internal static InteprocessSignal Create(string queueName, string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new WindowsSignal(queueName);

            return new UnixSignal(queueName, path);
        }
    }
}
