using System.Runtime.InteropServices;
using PKiller.Core.Model;

namespace PKiller.Core.Native;

internal static class NativeInterop
{
    public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int PROCESS_TERMINATE = 0x0001;
    public const int ERROR_ACCESS_DENIED = 5;

    private const int ProcessBasicInformation = 0;
    private const int ProcessIoCounters = 2;

    public const uint WM_CLOSE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int processAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(
        IntPtr processHandle,
        out long creationTime,
        out long exitTime,
        out long kernelTime,
        out long userTime);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION basicInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref IO_COUNTERS ioCounters,
        int processInformationLength,
        out int returnLength);

    public static int QueryParentPid(IntPtr handle)
    {
        var basicInformation = default(PROCESS_BASIC_INFORMATION);
        int status = NtQueryInformationProcess(handle, ProcessBasicInformation, ref basicInformation, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
        return status == 0 ? (int)basicInformation.InheritedFromUniqueProcessId : 0;
    }

    public static bool TryQueryIoCounters(IntPtr handle, out IO_COUNTERS counters)
    {
        counters = default;
        return NtQueryInformationProcess(
            handle,
            ProcessIoCounters,
            ref counters,
            Marshal.SizeOf<IO_COUNTERS>(),
            out _) == 0;
    }

    public static DateTimeOffset ToDateTimeOffset(long fileTime)
    {
        if (fileTime <= 0)
        {
            return DateTimeOffset.MinValue;
        }

        try
        {
            return DateTimeOffset.FromFileTime(fileTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    public static bool IsCurrentInstance(ProcessIdentity identity)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, identity.Pid);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return GetProcessTimes(handle, out long creation, out _, out _, out _)
                && ToDateTimeOffset(creation) == identity.StartTime;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc enumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}
