using System;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal interface IInterprocessSemaphore : IDisposable
    {
        ValueTask ReleaseAsync();
        bool WaitOne(int millisecondsTimeout);
    }
}
