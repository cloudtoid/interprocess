namespace Cloudtoid.Interprocess.Semaphore.MacOS;

internal sealed class SemaphoreMacOS : IInterprocessSemaphoreWaiter, IInterprocessSemaphoreReleaser
{
    private const string HandleNamePrefix = "/ct.ip.";
    private readonly string name;
    private readonly bool deleteOnDispose;
    private IntPtr handle;

    internal SemaphoreMacOS(string name, bool deleteOnDispose = false)
    {
        this.name = name = HandleNamePrefix + name;
        this.deleteOnDispose = deleteOnDispose;
        handle = Interop.CreateOrOpenSemaphore(name, 0);
    }

    ~SemaphoreMacOS() =>
        DisposeCore();

    public void Release() =>
        Interop.Release(handle);

    public bool Wait(int millisecondsTimeout) =>
        Interop.Wait(handle, millisecondsTimeout);

    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    internal static void Unlink(string name) =>
        Interop.Unlink(HandleNamePrefix + name);

    private void DisposeCore()
    {
        var current = Interlocked.Exchange(ref handle, IntPtr.Zero);
        if (current == IntPtr.Zero)
            return;

        Interop.Close(current);

        if (deleteOnDispose)
            Interop.Unlink(name);
    }
}