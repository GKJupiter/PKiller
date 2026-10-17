namespace PKiller.Core.Model;

/// <summary>
/// A stop target captured at selection time. The identities are fingerprints, so a delayed action
/// can never hit a process that reused a PID after the original app exited.
/// </summary>
public sealed record AppStopTarget(string Key, string DisplayName, IReadOnlyList<ProcessIdentity> Processes)
{
    public static AppStopTarget From(AppGroup group) =>
        new(group.Key, group.DisplayName, group.Processes.Select(p => p.Identity).ToList());
}
