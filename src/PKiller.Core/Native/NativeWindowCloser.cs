using System.Diagnostics;
using PKiller.Core.Abstractions;
using PKiller.Core.Model;

namespace PKiller.Core.Native;

/// <summary>
/// Polite close: posts WM_CLOSE to the process's visible top-level windows. Only the process that
/// was selected is touched, and the identity is re-checked before any message is sent.
/// </summary>
public sealed class NativeWindowCloser : IWindowCloser
{
    public CloseResult Close(ProcessIdentity identity)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(identity.Pid);
        }
        catch (ArgumentException)
        {
            return CloseResult.Fail("Process exited before the close request was sent.");
        }
        catch (Exception ex)
        {
            return CloseResult.Fail($"Could not open process: {ex.Message}");
        }

        using (process)
        {
            DateTimeOffset startTime;
            try
            {
                startTime = process.StartTime;
            }
            catch (Exception)
            {
                return CloseResult.Fail($"PID {identity.Pid} could not be verified; close skipped.");
            }

            if (startTime != identity.StartTime ||
                !string.Equals(process.ProcessName, identity.Name, StringComparison.OrdinalIgnoreCase))
            {
                return CloseResult.Fail($"PID {identity.Pid} now belongs to a different process; close skipped.");
            }

            int windows = 0;
            NativeInterop.EnumWindowsProc callback = (hwnd, _) =>
            {
                NativeInterop.GetWindowThreadProcessId(hwnd, out uint windowPid);
                if (windowPid == identity.Pid &&
                    NativeInterop.IsWindowVisible(hwnd) &&
                    NativeInterop.IsCurrentInstance(identity))
                {
                    if (NativeInterop.PostMessageW(hwnd, NativeInterop.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                    {
                        windows++;
                    }
                }

                return true;
            };

            try
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    NativeInterop.EnumThreadWindows((uint)thread.Id, callback, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                return CloseResult.Fail($"Could not enumerate windows: {ex.Message}");
            }

            return windows > 0
                ? CloseResult.Ok($"Close request sent to {windows} window(s).")
                : CloseResult.Fail("No closeable window accepted a close request — force-stop is available.");
        }
    }
}
