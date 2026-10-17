using PKiller.Core.Abstractions;
using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>
/// Orchestrates close and force-stop. Every action re-resolves the captured identity first, so a
/// delayed action can never target a process that reused a PID.
/// </summary>
public sealed class StopCoordinator
{
    /// <summary>Grace window after a close request before force-stop is offered.</summary>
    public static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(10);

    private readonly IProcessResolver _resolver;
    private readonly IWindowCloser _closer;
    private readonly IProcessTerminator _terminator;
    private readonly IClock _clock;

    public StopCoordinator(
        IProcessResolver resolver,
        IWindowCloser closer,
        IProcessTerminator terminator,
        IClock clock)
    {
        _resolver = resolver;
        _closer = closer;
        _terminator = terminator;
        _clock = clock;
    }

    /// <summary>
    /// Ask one app's processes to close politely. Apps with no closeable window are reported as
    /// still running so force-stop can be offered immediately.
    /// </summary>
    public async Task<GracefulCloseSession> CloseAsync(AppStopTarget target, CancellationToken cancellationToken)
    {
        var outcomes = new List<StopOutcome>();
        var candidates = new List<ProcessIdentity>();

        foreach (var identity in target.Processes)
        {
            var resolution = _resolver.Resolve(identity);

            switch (resolution.Status)
            {
                case ResolutionStatus.Exited:
                    outcomes.Add(new StopOutcome(identity, OutcomeStatus.AlreadyExited, "Process already exited."));
                    continue;
                case ResolutionStatus.StalePid:
                    outcomes.Add(new StopOutcome(identity, OutcomeStatus.SkippedStalePid,
                        $"PID {identity.Pid} now belongs to a different process ({resolution.Process?.Name}); skipped."));
                    continue;
                case ResolutionStatus.Unverifiable:
                    outcomes.Add(new StopOutcome(identity, OutcomeStatus.SkippedUnverified,
                        $"PID {identity.Pid} could not be verified; skipped."));
                    continue;
            }

            var process = resolution.Process!;

            if (!process.CanTerminate)
            {
                outcomes.Add(new StopOutcome(identity, OutcomeStatus.AccessDenied,
                    "Access denied — this process is owned by another user. PKiller does not elevate automatically."));
                continue;
            }

            if (!process.HasMainWindow)
            {
                outcomes.Add(new StopOutcome(identity, OutcomeStatus.CloseFailed,
                    "No window to close; force-stop is available for this process."));
                candidates.Add(identity);
                continue;
            }

            var result = _closer.Close(identity);
            if (!result.Success)
            {
                outcomes.Add(new StopOutcome(identity, OutcomeStatus.CloseFailed, result.Message));
                candidates.Add(identity);
                continue;
            }

            outcomes.Add(new StopOutcome(identity, OutcomeStatus.CloseRequested, result.Message));
            candidates.Add(identity);
        }

        if (outcomes.Any(o => o.Status == OutcomeStatus.CloseRequested))
        {
            await _clock.Delay(GraceWindow, cancellationToken);
        }

        var stillRunning = new List<ProcessIdentity>();
        for (var i = 0; i < outcomes.Count; i++)
        {
            var outcome = outcomes[i];
            if (outcome.Status != OutcomeStatus.CloseRequested)
            {
                if (outcome.Status == OutcomeStatus.CloseFailed && candidates.Contains(outcome.Identity))
                {
                    stillRunning.Add(outcome.Identity);
                }

                continue;
            }

            var resolution = _resolver.Resolve(outcome.Identity);
            if (resolution.Status == ResolutionStatus.Exited)
            {
                outcomes[i] = outcome with { Status = OutcomeStatus.Closed, Message = "Closed." };
            }
            else if (resolution.Status == ResolutionStatus.StalePid)
            {
                outcomes[i] = outcome with { Status = OutcomeStatus.SkippedStalePid, Message = "PID was reused by another process after the close request." };
            }
            else if (resolution.Status == ResolutionStatus.Unverifiable)
            {
                outcomes[i] = outcome with { Status = OutcomeStatus.SkippedUnverified, Message = "Process identity could not be verified after the close request." };
            }
            else if (resolution.Process is { CanTerminate: false })
            {
                outcomes[i] = outcome with
                {
                    Status = OutcomeStatus.AccessDenied,
                    Message = "Access to this process changed after the close request; force-stop is unavailable.",
                };
            }
            else
            {
                stillRunning.Add(outcome.Identity);
            }
        }

        return new GracefulCloseSession(target, outcomes, stillRunning);
    }

    /// <summary>Batch close: all selected apps are asked to close concurrently.</summary>
    public async Task<IReadOnlyList<GracefulCloseSession>> CloseBatchAsync(
        IReadOnlyList<AppStopTarget> targets,
        CancellationToken cancellationToken)
    {
        var tasks = targets.Select(t => CloseAsync(t, cancellationToken));
        return (await Task.WhenAll(tasks)).ToList();
    }

    /// <summary>Force-stop one app's process tree. Callers must have confirmed with the user first.</summary>
    public IReadOnlyList<StopOutcome> ForceStop(AppStopTarget target)
    {
        var outcomes = new List<StopOutcome>();

        foreach (var identity in target.Processes)
        {
            var resolution = _resolver.Resolve(identity);

            switch (resolution.Status)
            {
                case ResolutionStatus.Exited:
                    outcomes.Add(new StopOutcome(identity, OutcomeStatus.AlreadyExited, "Process already exited."));
                    continue;
                case ResolutionStatus.StalePid:
                    outcomes.Add(new StopOutcome(identity, OutcomeStatus.SkippedStalePid,
                        $"PID {identity.Pid} now belongs to a different process ({resolution.Process?.Name}); skipped."));
                    continue;
                case ResolutionStatus.Unverifiable:
                    outcomes.Add(new StopOutcome(identity, OutcomeStatus.SkippedUnverified,
                        $"PID {identity.Pid} could not be verified; skipped."));
                    continue;
            }

            var process = resolution.Process!;
            if (!process.CanTerminate)
            {
                outcomes.Add(new StopOutcome(identity, OutcomeStatus.AccessDenied,
                    "Access denied — this process is owned by another user. PKiller does not elevate automatically."));
                continue;
            }

            var result = _terminator.Terminate(identity);
            outcomes.Add(result.Success
                ? new StopOutcome(identity, OutcomeStatus.Terminated, result.Message)
                : new StopOutcome(identity, OutcomeStatus.TerminateFailed, result.Message));
        }

        return outcomes;
    }
}
