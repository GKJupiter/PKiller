namespace PKiller.Core.Model;

public enum StopAction
{
    /// <summary>Ask the app's windows to close politely.</summary>
    GracefulClose,

    /// <summary>Terminate the process tree without a chance to save.</summary>
    ForceStop
}

public enum ResolutionStatus
{
    Running,

    /// <summary>The process instance we targeted is gone.</summary>
    Exited,

    /// <summary>The PID now belongs to a different process instance (PID reuse).</summary>
    StalePid,

    /// <summary>The instance cannot be verified, so nothing is acted on.</summary>
    Unverifiable
}

public sealed record Resolution(ProcessIdentity Requested, ResolutionStatus Status, ProcessInfo? Process)
{
    public static Resolution Exited(ProcessIdentity identity) => new(identity, ResolutionStatus.Exited, null);
    public static Resolution Stale(ProcessIdentity identity, ProcessInfo current) => new(identity, ResolutionStatus.StalePid, current);
    public static Resolution Running(ProcessIdentity identity, ProcessInfo current) => new(identity, ResolutionStatus.Running, current);
}

public enum OutcomeStatus
{
    /// <summary>A close request was delivered to a window.</summary>
    CloseRequested,

    /// <summary>Process exited as a result of the close request.</summary>
    Closed,

    Terminated,

    AlreadyExited,

    /// <summary>Skipped because the PID was reused by an unrelated process.</summary>
    SkippedStalePid,

    /// <summary>Skipped because the process instance could not be verified.</summary>
    SkippedUnverified,

    /// <summary>Process is owned by another user or requires elevation.</summary>
    AccessDenied,

    CloseFailed,
    TerminateFailed
}

public sealed record StopOutcome(ProcessIdentity Identity, OutcomeStatus Status, string Message)
{
    /// <summary>True when the app was not actually stopped — never report a successful stop for these.</summary>
    public bool DidNotStop =>
        Status is OutcomeStatus.AlreadyExited
            or OutcomeStatus.SkippedStalePid
            or OutcomeStatus.SkippedUnverified
            or OutcomeStatus.AccessDenied
            or OutcomeStatus.CloseFailed
            or OutcomeStatus.TerminateFailed;
}

public sealed record GracefulCloseSession(
    AppStopTarget Target,
    IReadOnlyList<StopOutcome> Outcomes,
    IReadOnlyList<ProcessIdentity> StillRunning)
{
    /// <summary>Apps that did not close within the grace window and can now be force-stopped.</summary>
    public bool OffersForceStop => StillRunning.Count > 0;
}
