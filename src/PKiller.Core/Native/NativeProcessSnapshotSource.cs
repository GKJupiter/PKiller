using System.Diagnostics;
using System.ComponentModel;
using PKiller.Core.Abstractions;
using PKiller.Core.Model;
using PKiller.Core.Services;

namespace PKiller.Core.Native;

/// <summary>
/// Live process snapshot. Processes that vanish mid-enumeration are simply absent from the sample,
/// and processes we cannot query are marked inaccessible so the UI can show the limitation.
/// </summary>
public sealed class NativeProcessSnapshotSource : IProcessSnapshotSource
{
    private readonly ProcessClassifier _classifier = new();

    public int CurrentSessionId { get; }

    public NativeProcessSnapshotSource()
    {
        using var self = Process.GetCurrentProcess();
        CurrentSessionId = self.SessionId;
    }

    public IReadOnlyList<ProcessInfo> Read()
        => ReadCore(includeOnlyVisibleProcesses: false);

    /// <summary>Read the process details needed by the UI, skipping metrics for filtered processes.</summary>
    public IReadOnlyList<ProcessInfo> ReadForDisplay()
        => ReadCore(includeOnlyVisibleProcesses: true);

    private IReadOnlyList<ProcessInfo> ReadCore(bool includeOnlyVisibleProcesses)
    {
        var snapshot = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                ProcessInfo? info = ReadProcess(process, includeOnlyVisibleProcesses);
                if (info is not null)
                {
                    snapshot.Add(info);
                }
            }
            catch (Exception ex) when (IsExpectedProcessError(ex))
            {
                // The process vanished mid-enumeration; skip it rather than failing the whole sample.
            }
            finally
            {
                process.Dispose();
            }
        }

        return snapshot;
    }

    public ProcessInfo? Read(int pid)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (Exception ex) when (IsExpectedProcessError(ex))
        {
            return null;
        }

        using (process)
        {
            try
            {
                return ReadProcess(process, includeOnlyVisibleProcesses: false);
            }
            catch (Exception ex) when (IsExpectedProcessError(ex))
            {
                return null;
            }
        }
    }

    private ProcessInfo? ReadProcess(Process process, bool includeOnlyVisibleProcesses)
    {
        int pid = process.Id;
        string name;
        try
        {
            name = process.ProcessName;
        }
        catch (Exception ex) when (IsExpectedProcessError(ex))
        {
            name = $"PID {pid}";
        }

        if (includeOnlyVisibleProcesses && _classifier.IsCoreSystemProcess(name))
        {
            return null;
        }

        int sessionId;
        try
        {
            sessionId = process.SessionId;
        }
        catch (Exception ex) when (IsExpectedProcessError(ex))
        {
            sessionId = -1;
        }

        if (includeOnlyVisibleProcesses && _classifier.IsService(sessionId, CurrentSessionId))
        {
            return null;
        }

        long workingSet;
        bool workingSetAvailable;
        try
        {
            workingSet = process.WorkingSet64;
            workingSetAvailable = true;
        }
        catch (Exception ex) when (IsExpectedProcessError(ex))
        {
            workingSet = 0;
            workingSetAvailable = false;
        }

        bool hasMainWindow;
        try
        {
            hasMainWindow = process.MainWindowHandle != IntPtr.Zero;
        }
        catch (Exception ex) when (IsExpectedProcessError(ex))
        {
            hasMainWindow = false;
        }

        string? imagePath = null;
        try
        {
            imagePath = process.MainModule?.FileName;
        }
        catch (Exception ex) when (IsExpectedProcessError(ex) || ex is NotSupportedException)
        {
            // Unavailable for processes owned by another user or across a 32/64-bit boundary.
            imagePath = null;
        }

        int parentPid = 0;
        long kernelTicks = 0;
        long userTicks = 0;
        long readBytes = 0;
        long writeBytes = 0;
        long readOps = 0;
        long writeOps = 0;
        DateTimeOffset startTime = DateTimeOffset.MinValue;
        bool timesAvailable = false;
        bool ioAvailable = false;

        const int queryAndTerminate = NativeInterop.PROCESS_QUERY_LIMITED_INFORMATION | NativeInterop.PROCESS_TERMINATE;
        IntPtr handle = NativeInterop.OpenProcess(queryAndTerminate, false, pid);
        bool canTerminate = handle != IntPtr.Zero;
        if (!canTerminate)
        {
            handle = NativeInterop.OpenProcess(NativeInterop.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        }

        if (handle != IntPtr.Zero)
        {
            try
            {
                parentPid = NativeInterop.QueryParentPid(handle);

                if (NativeInterop.GetProcessTimes(handle, out long creation, out _, out long kernel, out long user))
                {
                    startTime = NativeInterop.ToDateTimeOffset(creation);
                    kernelTicks = kernel;
                    userTicks = user;
                    timesAvailable = startTime != DateTimeOffset.MinValue;
                }

                if (NativeInterop.TryQueryIoCounters(handle, out var counters))
                {
                    readOps = (long)counters.ReadOperationCount;
                    writeOps = (long)counters.WriteOperationCount;
                    readBytes = (long)counters.ReadTransferCount;
                    writeBytes = (long)counters.WriteTransferCount;
                    ioAvailable = true;
                }
            }
            finally
            {
                NativeInterop.CloseHandle(handle);
            }
        }

        return new ProcessInfo
        {
            Pid = pid,
            ParentPid = parentPid,
            Name = name,
            ImagePath = imagePath,
            SessionId = sessionId,
            StartTime = startTime,
            WorkingSetBytes = workingSet,
            KernelTimeTicks = kernelTicks,
            UserTimeTicks = userTicks,
            ReadBytes = readBytes,
            WriteBytes = writeBytes,
            ReadOperationCount = readOps,
            WriteOperationCount = writeOps,
            HasMainWindow = hasMainWindow,
            IsAccessible = workingSetAvailable && timesAvailable && ioAvailable,
            CanTerminate = timesAvailable && canTerminate,
        };
    }

    private static bool IsExpectedProcessError(Exception exception) =>
        exception is InvalidOperationException
            or ArgumentException
            or Win32Exception
            or UnauthorizedAccessException;
}
