using PKiller.Core.Abstractions;
using PKiller.Core.Model;
using PKiller.Core.Services;

namespace PKiller.Tests;

public sealed class StopCoordinatorTests
{
    [Fact]
    public async Task CloseBatchRequestsAllAppsBeforeWaitingForGracePeriod()
    {
        var first = Fixture.Process(10, 4, "alpha", @"C:\Apps\alpha.exe", hasMainWindow: true);
        var second = Fixture.Process(20, 4, "beta", @"C:\Apps\beta.exe", hasMainWindow: true);
        var source = new FakeSnapshotSource();
        source.Set(new[] { first, second });
        var closer = new FakeWindowCloser();
        var clock = new FakeClock();
        var coordinator = new StopCoordinator(new FakeResolver(source), closer, new FakeTerminator(), clock);
        var targets = new[]
        {
            AppStopTarget.From(Group(first)),
            AppStopTarget.From(Group(second)),
        };

        var sessions = await coordinator.CloseBatchAsync(targets, CancellationToken.None);

        Assert.Equal(2, closer.Closed.Count);
        Assert.Equal(2, sessions.Count);
        Assert.All(clock.Delays, delay => Assert.Equal(StopCoordinator.GraceWindow, delay));
    }

    [Fact]
    public async Task CloseOffersForceStopForProcessWithoutWindowImmediately()
    {
        var process = Fixture.Process(10, 4, "headless", @"C:\Apps\headless.exe");
        var source = new FakeSnapshotSource();
        source.Set(new[] { process });
        var closer = new FakeWindowCloser();
        var clock = new FakeClock();
        var coordinator = new StopCoordinator(new FakeResolver(source), closer, new FakeTerminator(), clock);

        var session = await coordinator.CloseAsync(AppStopTarget.From(Group(process)), CancellationToken.None);

        Assert.True(session.OffersForceStop);
        Assert.Empty(closer.Closed);
        Assert.Empty(clock.Delays);
    }

    [Fact]
    public async Task ClosedProcessIsNotOfferedForceStop()
    {
        var process = Fixture.Process(10, 4, "app", @"C:\Apps\app.exe", hasMainWindow: true);
        var source = new FakeSnapshotSource();
        source.Set(new[] { process });
        var closer = new FakeWindowCloser
        {
            Behavior = identity =>
            {
                source.Remove(identity.Pid);
                return CloseResult.Ok("Close request sent.");
            },
        };
        var coordinator = new StopCoordinator(new FakeResolver(source), closer, new FakeTerminator(), new FakeClock());

        var session = await coordinator.CloseAsync(AppStopTarget.From(Group(process)), CancellationToken.None);

        Assert.False(session.OffersForceStop);
        Assert.Equal(OutcomeStatus.Closed, Assert.Single(session.Outcomes).Status);
    }

    [Fact]
    public async Task PidReusedDuringGraceWindowIsNeverOfferedOrForceStopped()
    {
        var process = Fixture.Process(10, 4, "app", @"C:\Apps\app.exe", hasMainWindow: true);
        var replacement = Fixture.Process(10, 4, "unrelated", @"C:\Apps\unrelated.exe", hasMainWindow: true);
        var source = new FakeSnapshotSource();
        source.Set(new[] { process });
        var clock = new FakeClock();
        var coordinator = new StopCoordinator(
            new FakeResolver(source),
            new FakeWindowCloser
            {
                Behavior = identity =>
                {
                    source.ReusePid(identity.Pid, replacement);
                    return CloseResult.Ok("Close request sent.");
                },
            },
            new FakeTerminator(),
            clock);

        var session = await coordinator.CloseAsync(AppStopTarget.From(Group(process)), CancellationToken.None);

        Assert.False(session.OffersForceStop);
        Assert.Equal(OutcomeStatus.SkippedStalePid, Assert.Single(session.Outcomes).Status);
    }

    [Fact]
    public void ForceStopRevalidatesIdentityBeforeTermination()
    {
        var process = Fixture.Process(10, 4, "app", @"C:\Apps\app.exe");
        var replacement = Fixture.Process(10, 4, "unrelated", @"C:\Apps\unrelated.exe");
        var source = new FakeSnapshotSource();
        source.Set(new[] { replacement });
        var terminator = new FakeTerminator();
        var coordinator = new StopCoordinator(new FakeResolver(source), new FakeWindowCloser(), terminator, new FakeClock());

        var outcomes = coordinator.ForceStop(AppStopTarget.From(Group(process)));

        Assert.Empty(terminator.Terminated);
        Assert.Equal(OutcomeStatus.SkippedStalePid, Assert.Single(outcomes).Status);
    }

    [Fact]
    public void TerminationFailureIsNotReportedAsSuccess()
    {
        var process = Fixture.Process(10, 4, "app", @"C:\Apps\app.exe");
        var source = new FakeSnapshotSource();
        source.Set(new[] { process });
        var terminator = new FakeTerminator
        {
            Behavior = _ => TerminateResult.Fail("Access denied."),
        };
        var coordinator = new StopCoordinator(new FakeResolver(source), new FakeWindowCloser(), terminator, new FakeClock());

        var outcome = Assert.Single(coordinator.ForceStop(AppStopTarget.From(Group(process))));

        Assert.Equal(OutcomeStatus.TerminateFailed, outcome.Status);
        Assert.True(outcome.DidNotStop);
    }

    [Fact]
    public void InaccessibleProcessesAreNotActedUpon()
    {
        var process = Fixture.Process(10, 4, "app", @"C:\Apps\app.exe", isAccessible: false);
        var source = new FakeSnapshotSource();
        source.Set(new[] { process });
        var terminator = new FakeTerminator();
        var coordinator = new StopCoordinator(new FakeResolver(source), new FakeWindowCloser(), terminator, new FakeClock());

        var outcome = Assert.Single(coordinator.ForceStop(AppStopTarget.From(Group(process))));

        Assert.Equal(OutcomeStatus.AccessDenied, outcome.Status);
        Assert.Empty(terminator.Terminated);
    }

    private static AppGroup Group(ProcessInfo process) =>
        new()
        {
            Key = process.GroupKey,
            DisplayName = Path.GetFileNameWithoutExtension(process.ImagePath) ?? process.Name,
            Roots = new[] { process },
            Processes = new[] { process },
            HasCloseableWindow = process.HasMainWindow,
            IsAccessible = process.IsAccessible,
            CanTerminate = process.CanTerminate,
        };
}
