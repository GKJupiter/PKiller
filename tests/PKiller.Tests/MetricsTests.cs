using PKiller.Core.Model;
using PKiller.Core.Services;

namespace PKiller.Tests;

public sealed class MetricsTests
{
    private static AppGrouper Grouper() => new(new ProcessClassifier(), new MetricsAggregator());

    [Fact]
    public void AppRowAggregatesCpuMemoryAndIoOverIncludedChildren()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "app", @"C:\Apps\app.exe",
                workingSetBytes: 100 * 1024 * 1024, cpuPercent: 2.5, readBytes: 1000, writeBytes: 500, hasMainWindow: true),
            Fixture.Process(11, 10, "app", @"C:\Apps\app.exe",
                workingSetBytes: 40 * 1024 * 1024, cpuPercent: 1.5, readBytes: 200, writeBytes: 100),
            Fixture.Process(12, 10, "app", @"C:\Apps\app.exe",
                workingSetBytes: 10 * 1024 * 1024, cpuPercent: 0.5, readBytes: 50, writeBytes: 25),
        };

        var group = Assert.Single(Grouper().Group(snapshot, 1));

        Assert.Equal(4.5, group.CpuPercent);
        Assert.Equal(150 * 1024 * 1024, group.WorkingSetBytes);
        Assert.Equal(1250, group.ReadBytes);
        Assert.Equal(625, group.WriteBytes);
    }

    [Fact]
    public void WorkingSetIsReportedInMegabytes()
    {
        Assert.Equal(150.0, MetricsAggregator.WorkingSetMb(150 * 1024 * 1024));
    }

    [Fact]
    public void GroupIsMarkedUnavailableWhenAnyChildCannotBeAccessed()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "app", @"C:\Apps\app.exe", hasMainWindow: true, isAccessible: false),
            Fixture.Process(11, 10, "app", @"C:\Apps\app.exe", isAccessible: true),
        };

        var group = Assert.Single(Grouper().Group(snapshot, 1));
        Assert.False(group.IsAccessible);
    }

    [Fact]
    public void CloseableWindowFlagComesFromAnyProcessInTheGroup()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "app", @"C:\Apps\app.exe", hasMainWindow: false),
            Fixture.Process(11, 10, "app", @"C:\Apps\app.exe", hasMainWindow: true),
        };

        var group = Assert.Single(Grouper().Group(snapshot, 1));
        Assert.True(group.HasCloseableWindow);
    }

    [Fact]
    public void CpuIsNormalisedToWholeMachineCapacity()
    {
        // 8 logical processors. One process busy for 1s of CPU time over a 1s window = 12.5% of the machine.
        var sampler = new CpuSampler(8);
        var baseline = Fixture.Process(10, 4, "app", @"C:\Apps\app.exe", kernelTimeTicks: 0, userTimeTicks: 0);
        sampler.Sample(new[] { baseline }, 1.0);

        var second = baseline with { KernelTimeTicks = 5_000_000, UserTimeTicks = 5_000_000 };
        var sampled = sampler.Sample(new[] { second }, 1.0);

        Assert.Equal(12.5, sampled[0].CpuPercent, 3);
    }

    [Fact]
    public void ProcessUsingMultipleCoresIsNormalisedToWholeMachineCapacity()
    {
        var sampler = new CpuSampler(8);
        var baseline = Fixture.Process(10, 4, "app", kernelTimeTicks: 0, userTimeTicks: 0);
        sampler.Sample(new[] { baseline }, 1.0);

        // Four cores fully busy for one second use half of an eight-core machine's capacity.
        var second = baseline with { KernelTimeTicks = 20_000_000, UserTimeTicks = 20_000_000 };
        var sampled = sampler.Sample(new[] { second }, 1.0);

        Assert.Equal(50, sampled[0].CpuPercent, 3);
    }

    [Fact]
    public void ProcessCpuNeverExceedsOneHundredPercent()
    {
        var sampler = new CpuSampler(8);
        var baseline = Fixture.Process(10, 4, "app", kernelTimeTicks: 0, userTimeTicks: 0);
        sampler.Sample(new[] { baseline }, 1.0);

        // More than eight seconds of process CPU in a one-second window is an inconsistent sample.
        var second = baseline with { KernelTimeTicks = 100_000_000, UserTimeTicks = 0 };
        var sampled = sampler.Sample(new[] { second }, 1.0);

        Assert.Equal(100, sampled[0].CpuPercent);
        Assert.InRange(sampled[0].CpuPercent, 0, 100);
    }

    [Fact]
    public void AppCpuAggregatesChildrenButNeverExceedsOneHundredPercent()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "app", @"C:\Apps\app.exe", cpuPercent: 80, hasMainWindow: true),
            Fixture.Process(11, 10, "app", @"C:\Apps\app.exe", cpuPercent: 80),
        };

        var group = Assert.Single(Grouper().Group(snapshot, 1));

        Assert.Equal(100, group.CpuPercent);
        Assert.InRange(group.CpuPercent, 0, 100);
        Assert.Equal(80, group.Processes[0].CpuPercent);
        Assert.Equal(80, group.Processes[1].CpuPercent);
    }

    [Fact]
    public void FirstSampleHasNoBaselineSoCpuIsZero()
    {
        var sampler = new CpuSampler(4);
        var sampled = sampler.Sample(new[] { Fixture.Process(10, 4, "app", kernelTimeTicks: 100_000, userTimeTicks: 100_000) }, 1.0);
        Assert.Equal(0, sampled[0].CpuPercent);
    }

    [Fact]
    public void ResetAfterPausePreventsASpikeFromStaleBaseline()
    {
        var sampler = new CpuSampler(4);
        var first = Fixture.Process(10, 4, "app", kernelTimeTicks: 0, userTimeTicks: 0);
        sampler.Sample(new[] { first }, 1.0);

        var second = first with { KernelTimeTicks = 40_000, UserTimeTicks = 40_000 };
        sampler.Sample(new[] { second }, 10.0);

        // After a pause the baseline is dropped, so the next sample reports 0 rather than the whole gap.
        sampler.Reset();
        var third = sampler.Sample(new[] { second with { KernelTimeTicks = 80_000, UserTimeTicks = 80_000 } }, 1.0);
        Assert.Equal(0, third[0].CpuPercent);
    }
}
