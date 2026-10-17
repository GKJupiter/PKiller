using PKiller.Core.Model;
using PKiller.Core.Services;

namespace PKiller.Tests;

public sealed class ProcessListPresenterTests
{
    [Fact]
    public void SortsEveryDisplayedColumnInRequestedDirection()
    {
        var presenter = new ProcessListPresenter(
            new AppGrouper(new ProcessClassifier(), new MetricsAggregator()));
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "alpha", @"C:\Apps\alpha.exe",
                workingSetBytes: 100, cpuPercent: 1, readBytes: 10, writeBytes: 20, hasMainWindow: true),
            Fixture.Process(20, 4, "beta", @"C:\Apps\beta.exe",
                workingSetBytes: 200, cpuPercent: 3, readBytes: 30, writeBytes: 10, hasMainWindow: true),
        };

        AssertOrder(SortMetric.Name, SortDirection.Ascending, "alpha", "beta");
        AssertOrder(SortMetric.Cpu, SortDirection.Descending, "beta", "alpha");
        AssertOrder(SortMetric.Memory, SortDirection.Descending, "beta", "alpha");
        AssertOrder(SortMetric.Read, SortDirection.Descending, "beta", "alpha");
        AssertOrder(SortMetric.Write, SortDirection.Descending, "alpha", "beta");

        void AssertOrder(SortMetric metric, SortDirection direction, string first, string second)
        {
            var result = presenter.Present(snapshot, 1, null, new SortOrder(metric, direction));
            Assert.Equal(new[] { first, second }, result.Select(g => g.DisplayName));
        }
    }

    [Fact]
    public void DefaultSortIsHighestMemoryFirst()
    {
        Assert.Equal(SortMetric.Memory, SortOrder.Default.Metric);
        Assert.Equal(SortDirection.Descending, SortOrder.Default.Direction);
    }
}
