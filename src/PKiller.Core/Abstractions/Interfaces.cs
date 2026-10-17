using PKiller.Core.Model;

namespace PKiller.Core.Abstractions;

/// <summary>Source of process snapshots. Abstracted so grouping/metrics logic can be tested with fixtures.</summary>
public interface IProcessSnapshotSource
{
    int CurrentSessionId { get; }

    /// <summary>Snapshot the current process table. Processes that vanish mid-enumeration are simply absent.</summary>
    IReadOnlyList<ProcessInfo> Read();

    /// <summary>Read one process by PID for identity revalidation immediately before an action.</summary>
    ProcessInfo? Read(int pid);
}

/// <summary>Resolves a captured identity against the live process table immediately before acting.</summary>
public interface IProcessResolver
{
    Resolution Resolve(ProcessIdentity identity);
}

/// <summary>Delivers a polite close request to a process's top-level windows.</summary>
public interface IWindowCloser
{
    CloseResult Close(ProcessIdentity identity);
}

/// <summary>Terminates a process instance. Never elevates.</summary>
public interface IProcessTerminator
{
    TerminateResult Terminate(ProcessIdentity identity);
}

public sealed record CloseResult(bool Success, string Message)
{
    public static CloseResult Ok(string message) => new(true, message);
    public static CloseResult Fail(string message) => new(false, message);
}

public sealed record TerminateResult(bool Success, string Message)
{
    public static TerminateResult Ok(string message) => new(true, message);
    public static TerminateResult Fail(string message) => new(false, message);
}

/// <summary>Injectable clock so the 10 second grace window is testable.</summary>
public interface IClock
{
    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed record AppSettings(
    SortOrder Sort,
    bool AutoRefresh,
    IReadOnlyList<TableColumn> ColumnOrder)
{
    public static AppSettings Default { get; } = new(
        SortOrder.Default,
        true,
        [
            TableColumn.Name,
            TableColumn.Cpu,
            TableColumn.Memory,
            TableColumn.Read,
            TableColumn.Write,
            TableColumn.Actions,
        ]);
}

/// <summary>
/// Elevation is never automatic. The app only elevates after the user explicitly chooses the
/// relaunch-as-administrator option.
/// </summary>
public interface IElevationService
{
    bool IsElevated { get; }

    /// <summary>True when a relaunch is possible from the current (non-elevated) context.</summary>
    bool CanRelaunchAsAdministrator { get; }

    /// <summary>Must only be called after the user explicitly chooses it.</summary>
    bool RelaunchAsAdministrator();
}
