using System;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal interface IInteprocessSignal : IDisposable
    {
        ValueTask SignalAsync();
        bool Wait(int millisecondsTimeout);
    }
}
