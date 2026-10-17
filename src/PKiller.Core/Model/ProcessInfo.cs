namespace PKiller.Core.Model;

/// <summary>
/// A single sampled process instance. Identity is (Pid, StartTime, Name) rather than the PID alone,
/// because Windows reuses PIDs once a process exits.
/// </summary>
public sealed record ProcessInfo
{
    public required int Pid { get; init; }
    public required int ParentPid { get; init; }
    public required string Name { get; init; }

    /// <summary>Null when the image path is not readable (process owned by another user).</summary>
    public string? ImagePath { get; init; }

    public int SessionId { get; init; }
    public DateTimeOffset StartTime { get; init; }

    public long WorkingSetBytes { get; init; }

    /// <summary>Raw CPU time counters in 100 ns units, used to compute the normalised sample.</summary>
    public long KernelTimeTicks { get; init; }
    public long UserTimeTicks { get; init; }

    /// <summary>Normalised to total machine capacity: 100% means the whole machine, not one logical processor.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Process-level read/write accounting, not physical-disk-only bytes.</summary>
    public long ReadBytes { get; init; }
    public long WriteBytes { get; init; }
    public long ReadOperationCount { get; init; }
    public long WriteOperationCount { get; init; }

    public bool HasMainWindow { get; init; }

    /// <summary>False when one or more process metrics are unavailable.</summary>
    public bool IsAccessible { get; init; }

    /// <summary>Whether this process can be safely targeted for termination without elevation.</summary>
    public bool CanTerminate { get; init; }

    public ProcessIdentity Identity => new(Pid, StartTime, Name);

    public string GroupKey => (ImagePath ?? Name).ToLowerInvariant();
}

/// <summary>Fingerprint used to prove a PID still refers to the same process instance.</summary>
public sealed record ProcessIdentity(int Pid, DateTimeOffset StartTime, string Name)
{
    public bool SameInstance(ProcessIdentity other) =>
        Pid == other.Pid &&
        StartTime == other.StartTime &&
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
}
