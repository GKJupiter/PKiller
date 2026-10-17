using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>Search and sorting for the app rows. Pure so the behaviour is directly testable.</summary>
public sealed class ProcessListPresenter
{
    private readonly AppGrouper _grouper;

    public ProcessListPresenter(AppGrouper grouper)
    {
        _grouper = grouper;
    }

    public IReadOnlyList<AppGroup> Present(
        IEnumerable<ProcessInfo> snapshot,
        int currentSessionId,
        string? search,
        SortOrder sort)
    {
        var groups = _grouper.Group(snapshot, currentSessionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            groups = groups
                .Where(g => g.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || g.Processes.Any(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return Sort(groups, sort);
    }

    public static IReadOnlyList<AppGroup> Sort(IEnumerable<AppGroup> groups, SortOrder sort)
    {
        var ordered = sort.Metric switch
        {
            SortMetric.Name => sort.Direction == SortDirection.Descending
                ? groups.OrderByDescending(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                : groups.OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase),
            SortMetric.Cpu => OrderByMetric(groups, g => g.CpuPercent, sort.Direction),
            SortMetric.Memory => OrderByMetric(groups, g => g.WorkingSetBytes, sort.Direction),
            SortMetric.Read => OrderByMetric(groups, g => g.ReadBytes, sort.Direction),
            SortMetric.Write => OrderByMetric(groups, g => g.WriteBytes, sort.Direction),
            _ => groups.OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.ToList();
    }

    private static IOrderedEnumerable<AppGroup> OrderByMetric<TValue>(
        IEnumerable<AppGroup> groups,
        Func<AppGroup, TValue> selector,
        SortDirection direction)
        where TValue : IComparable<TValue> =>
        direction == SortDirection.Descending
            ? groups.OrderByDescending(selector)
            : groups.OrderBy(selector);
}
