using System;

namespace Cloudtoid.Interprocess
{
    internal interface IInterprocessSemaphoreReleaser : IDisposable
    {
        void Release();
    }
}
