namespace PKiller.Core.Model;

/// <summary>An app row: one app and the process tree that belongs to it.</summary>
public sealed record AppGroup
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Root processes of the app (a group can hold more than one tree, e.g. two launched instances).</summary>
    public required IReadOnlyList<ProcessInfo> Roots { get; init; }

    /// <summary>Roots first, then their descendants.</summary>
    public required IReadOnlyList<ProcessInfo> Processes { get; init; }

    public double CpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public long ReadBytes { get; init; }
    public long WriteBytes { get; init; }
    public bool HasCloseableWindow { get; init; }
    public bool IsAccessible { get; init; }
    public bool CanTerminate { get; init; }

    public int ProcessCount => Processes.Count;
}
