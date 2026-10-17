namespace PKiller.Core.Model;

public enum SortMetric
{
    Name,
    Cpu,
    Memory,
    Read,
    Write
}

public enum SortDirection
{
    Ascending,
    Descending
}

public sealed record SortOrder(SortMetric Metric, SortDirection Direction)
{
    /// <summary>Default sort required by the brief: highest memory first.</summary>
    public static SortOrder Default { get; } = new(SortMetric.Memory, SortDirection.Descending);
}
