using PKiller.Core.Model;
using PKiller.Core.Services;

namespace PKiller.Tests;

public sealed class StopPlannerTests
{
    private static StopPlanner Planner() => new();

    [Fact]
    public void SingleGracefulCloseNeedsNoConfirmation()
    {
        var group = Group(hasCloseableWindow: true);
        var planner = Planner();
        var action = planner.ActionFor(group);

        Assert.Equal(StopAction.GracefulClose, action);
        Assert.False(planner.RequiresConfirmation(new[] { AppStopTarget.From(group) }, action));
    }

    [Fact]
    public void BatchGracefulCloseRequiresConfirmation()
    {
        var planner = Planner();
        var targets = new[]
        {
            AppStopTarget.From(Group(hasCloseableWindow: true)),
            AppStopTarget.From(Group(key: "b", hasCloseableWindow: true)),
        };

        Assert.True(planner.RequiresConfirmation(targets, StopAction.GracefulClose));
    }

    [Fact]
    public void AppWithNoCloseableWindowOffersForceStopImmediately()
    {
        var group = Group(hasCloseableWindow: false);
        Assert.Equal(StopAction.ForceStop, Planner().ActionFor(group));
    }

    [Fact]
    public void ForceStopAlwaysRequiresConfirmation()
    {
        var group = Group(hasCloseableWindow: true);
        Assert.True(Planner().RequiresConfirmation(new[] { AppStopTarget.From(group) }, StopAction.ForceStop));
    }

    [Fact]
    public void ForceStopConfirmationNamesEveryAppAndWarnsAboutUnsavedWork()
    {
        var targets = new[]
        {
            AppStopTarget.From(Group(key: "notepad", displayName: "notepad")),
            AppStopTarget.From(Group(key: "word", displayName: "word")),
        };

        var message = Planner().ConfirmationMessage(targets, StopAction.ForceStop);

        Assert.Contains("notepad", message);
        Assert.Contains("word", message);
        Assert.Contains("unsaved work", message);
    }

    [Fact]
    public void GracefulBatchConfirmationNamesTheExactAppsAffected()
    {
        var targets = new[]
        {
            AppStopTarget.From(Group(key: "chrome", displayName: "chrome")),
            AppStopTarget.From(Group(key: "spotify", displayName: "spotify")),
        };

        var message = Planner().ConfirmationMessage(targets, StopAction.GracefulClose);

        Assert.Contains("chrome", message);
        Assert.Contains("spotify", message);
        Assert.DoesNotContain("unsaved work", message);
    }

    private static AppGroup Group(string key = "app", string displayName = "app", bool hasCloseableWindow = true) =>
        new()
        {
            Key = key,
            DisplayName = displayName,
            Roots = new[] { Fixture.Process(1, 0, displayName, $@"C:\Apps\{displayName}.exe", hasMainWindow: hasCloseableWindow) },
            Processes = new[] { Fixture.Process(1, 0, displayName, $@"C:\Apps\{displayName}.exe", hasMainWindow: hasCloseableWindow) },
            HasCloseableWindow = hasCloseableWindow,
            IsAccessible = true,
            CanTerminate = true,
        };
}
