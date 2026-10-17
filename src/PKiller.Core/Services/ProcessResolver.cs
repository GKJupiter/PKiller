using PKiller.Core.Abstractions;
using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>
/// Resolves a captured identity against the live process table immediately before acting.
/// A PID that now belongs to a different instance is reported as stale and never acted on.
/// </summary>
public sealed class ProcessResolver : IProcessResolver
{
    private readonly IProcessSnapshotSource _source;

    public ProcessResolver(IProcessSnapshotSource source)
    {
        _source = source;
    }

    public Resolution Resolve(ProcessIdentity identity)
    {
        var current = _source.Read(identity.Pid);

        if (current is null)
        {
            return Resolution.Exited(identity);
        }

        // An inaccessible process cannot be verified, so its identity is not trusted for action.
        if (current.StartTime == DateTimeOffset.MinValue)
        {
            return new Resolution(identity, ResolutionStatus.Unverifiable, current);
        }

        if (!current.Identity.SameInstance(identity))
        {
            return Resolution.Stale(identity, current);
        }

        return Resolution.Running(identity, current);
    }
}
