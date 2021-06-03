using AccessControlType = System.Security.AccessControl.AccessControlType;
using Debug = System.Diagnostics.Debug;
using OS = System.Runtime.InteropServices.OSPlatform;
using RunTimeInfo = System.Runtime.InteropServices.RuntimeInformation;
using SecurityIdentifier = System.Security.Principal.SecurityIdentifier;
using SemaphoreAccessRule = System.Security.AccessControl.SemaphoreAccessRule;
using SemaphoreRights = System.Security.AccessControl.SemaphoreRights;
using SemaphoreSecurity = System.Security.AccessControl.SemaphoreSecurity;
using SysSemaphore = System.Threading.Semaphore;
using ThreadingAclExtensions = System.Threading.ThreadingAclExtensions;
using WellKnownSidType = System.Security.Principal.WellKnownSidType;
namespace Cloudtoid.Interprocess.Semaphore.Windows
{
    // just a wrapper over the Windows named semaphore
    internal sealed class SemaphoreWindows : IInterprocessSemaphoreWaiter, IInterprocessSemaphoreReleaser
    {
        private const string HandleNamePrefix = @"Global\CT.IP.";
        private readonly SysSemaphore handle;

        internal SemaphoreWindows(string name)
        {
            /*
             * handle = new SysSemaphore(0, int.MaxValue, HandleNamePrefix + name);
             */
            //2021-06-02 Fabian Ramirez - Adding code to handle permissions when accessing the semaphore from different windows accounts
            string semaphorename = HandleNamePrefix + name;

            handle = new SysSemaphore(0, int.MaxValue, semaphorename);
            if (RunTimeInfo.IsOSPlatform(OS.Windows))
            {
                try
                {
                    SemaphoreSecurity semaphoreSecurity = new SemaphoreSecurity();
                    var sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                    semaphoreSecurity.AddAccessRule(new SemaphoreAccessRule(sid, SemaphoreRights.FullControl, AccessControlType.Allow));
                    ThreadingAclExtensions.SetAccessControl(handle, semaphoreSecurity);
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine("Exception Setting Semaphore Security");
                    Debug.WriteLine(ex.Message);
                    if (ex.StackTrace != null)
                        Debug.WriteLine(ex.StackTrace.ToString());
                }
            }

            //2021-06-02 Fabian Ramirez - END
        }

        public void Dispose()
            => handle.Dispose();

        public void Release()
            => handle.Release();

        public bool Wait(int millisecondsTimeout)
            => handle.WaitOne(millisecondsTimeout);
    }
}