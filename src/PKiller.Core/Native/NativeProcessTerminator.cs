using System.Runtime.InteropServices;
using PKiller.Core.Abstractions;
using PKiller.Core.Model;

namespace PKiller.Core.Native;

/// <summary>
/// Terminates one process instance. The handle is opened for that PID only, and the instance is
/// re-verified against the captured identity before TerminateProcess is called. No elevation.
/// </summary>
public sealed class NativeProcessTerminator : IProcessTerminator
{
    public TerminateResult Terminate(ProcessIdentity identity)
    {
        IntPtr handle = NativeInterop.OpenProcess(
            NativeInterop.PROCESS_TERMINATE | NativeInterop.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            identity.Pid);

        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return error == NativeInterop.ERROR_ACCESS_DENIED
                ? TerminateResult.Fail($"Access denied for PID {identity.Pid}. PKiller does not elevate automatically; use the relaunch-as-administrator option.")
                : TerminateResult.Fail($"Could not open PID {identity.Pid} (Win32 error {error}).");
        }

        try
        {
            if (!NativeInterop.GetProcessTimes(handle, out long creation, out _, out _, out _))
            {
                int error = Marshal.GetLastWin32Error();
                return TerminateResult.Fail($"Could not verify PID {identity.Pid} (Win32 error {error}); termination skipped.");
            }

            var current = NativeInterop.ToDateTimeOffset(creation);
            if (current == DateTimeOffset.MinValue || current != identity.StartTime)
            {
                return TerminateResult.Fail($"PID {identity.Pid} was reused or could not be verified; termination skipped.");
            }

            if (!NativeInterop.TerminateProcess(handle, 1))
            {
                int error = Marshal.GetLastWin32Error();
                return error == NativeInterop.ERROR_ACCESS_DENIED
                    ? TerminateResult.Fail($"Access denied terminating PID {identity.Pid}.")
                    : TerminateResult.Fail($"TerminateProcess failed for PID {identity.Pid} (Win32 error {error}).");
            }

            return TerminateResult.Ok($"PID {identity.Pid} terminated.");
        }
        finally
        {
            NativeInterop.CloseHandle(handle);
        }
    }
}
