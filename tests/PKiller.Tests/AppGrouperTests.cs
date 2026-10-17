using PKiller.Core.Model;
using PKiller.Core.Services;

namespace PKiller.Tests;

public sealed class AppGrouperTests
{
    private static AppGrouper Grouper() => new(new ProcessClassifier(), new MetricsAggregator());

    [Theory]
    [InlineData("notepad", 1, 1, true)]
    [InlineData("notepad", 2, 1, false)]
    [InlineData("svchost", 1, 1, false)]
    [InlineData("notepad", -1, 1, true)]
    public void NameAndSessionClassifierMatchesProcessClassification(
        string name,
        int sessionId,
        int currentSessionId,
        bool expected)
    {
        var process = Fixture.Process(10, 4, name, sessionId: sessionId);
        var classifier = new ProcessClassifier();

        Assert.Equal(expected, classifier.IsIncluded(process, currentSessionId));
        Assert.Equal(expected, classifier.IsIncluded(name, sessionId, currentSessionId));
    }

    [Fact]
    public void GroupsProcessesUnderTheirAppRoot()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "chrome", @"C:\Apps\chrome.exe", hasMainWindow: true),
            Fixture.Process(11, 10, "chrome", @"C:\Apps\chrome.exe"),
            Fixture.Process(12, 10, "chrome", @"C:\Apps\chrome.exe"),
        };

        var groups = Grouper().Group(snapshot, 1);

        var group = Assert.Single(groups);
        Assert.Equal("chrome", group.DisplayName);
        Assert.Single(group.Roots);
        Assert.Equal(3, group.ProcessCount);
        Assert.Equal(10, group.Roots[0].Pid);
    }

    [Fact]
    public void AppsLaunchedByAnExcludedParentBecomeTheirOwnRoot()
    {
        // Foreground apps launched by the Windows shell are separate app rows, not children of Explorer.
        var snapshot = new[]
        {
            Fixture.Process(4, 0, "explorer", @"C:\Windows\Explorer.EXE", hasMainWindow: true),
            Fixture.Process(20, 4, "widget", @"C:\Apps\widget.exe", hasMainWindow: true),
            Fixture.Process(21, 20, "widget", @"C:\Apps\widget.exe"),
        };

        var groups = Grouper().Group(snapshot, 1);

        Assert.Equal(2, groups.Count);
        var group = Assert.Single(groups, g => g.DisplayName == "widget");
        Assert.Equal(20, group.Roots[0].Pid);
        Assert.Equal(2, group.ProcessCount);
    }

    [Fact]
    public void TwoInstancesOfTheSameAppMergeIntoOneGroupWithBothRoots()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "notepad", @"C:\Apps\notepad.exe", hasMainWindow: true),
            Fixture.Process(20, 4, "notepad", @"C:\Apps\notepad.exe", hasMainWindow: true),
            Fixture.Process(21, 20, "notepad", @"C:\Apps\notepad.exe"),
        };

        var groups = Grouper().Group(snapshot, 1);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Roots.Count);
        Assert.Equal(3, group.ProcessCount);
    }

    [Fact]
    public void ServicesAndCoreSystemProcessesAreExcluded()
    {
        var snapshot = new[]
        {
            Fixture.Process(1, 0, "svchost", @"C:\Windows\System32\svchost.exe", sessionId: 0),
            Fixture.Process(2, 0, "System", null, sessionId: 0),
            Fixture.Process(3, 0, "dwm", @"C:\Windows\System32\dwm.exe", sessionId: 1),
            Fixture.Process(4, 0, "notepad", @"C:\Apps\notepad.exe", sessionId: 1, hasMainWindow: true),
        };

        var groups = Grouper().Group(snapshot, 1);
        var group = Assert.Single(groups);
        Assert.Equal("notepad", group.DisplayName);
    }

    [Fact]
    public void ProcessesFromAnotherSessionAreExcluded()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "otheruser", @"C:\Apps\other.exe", sessionId: 2, hasMainWindow: true),
            Fixture.Process(11, 4, "mine", @"C:\Apps\mine.exe", sessionId: 1, hasMainWindow: true),
        };

        var groups = Grouper().Group(snapshot, 1);
        var group = Assert.Single(groups);
        Assert.Equal("mine", group.DisplayName);
    }

    [Fact]
    public void ProcessWithUnknownSessionIsShownAsUnavailableRatherThanSilentlyDropped()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "unknown", @"C:\Apps\unknown.exe", sessionId: -1, isAccessible: false),
        };

        var group = Assert.Single(Grouper().Group(snapshot, 1));
        Assert.Equal("unknown", group.DisplayName);
        Assert.False(group.IsAccessible);
    }

    [Fact]
    public void ParentCycleDoesNotHideTheTree()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 11, "app", @"C:\Apps\app.exe", hasMainWindow: true),
            Fixture.Process(11, 10, "app", @"C:\Apps\app.exe"),
        };

        var groups = Grouper().Group(snapshot, 1);
        var group = Assert.Single(groups);
        Assert.Equal(2, group.ProcessCount);
    }

    [Fact]
    public void ForegroundChildAppIsNotIncludedInItsParentsProcessTree()
    {
        var snapshot = new[]
        {
            Fixture.Process(4, 0, "explorer", @"C:\Windows\explorer.exe", hasMainWindow: true),
            Fixture.Process(10, 4, "launcher", @"C:\Apps\launcher.exe", hasMainWindow: true),
            Fixture.Process(20, 10, "editor", @"C:\Apps\editor.exe", hasMainWindow: true),
            Fixture.Process(21, 20, "editor", @"C:\Apps\editor.exe"),
        };

        var groups = Grouper().Group(snapshot, 1);
        var launcher = Assert.Single(groups, g => g.DisplayName == "launcher");
        var editor = Assert.Single(groups, g => g.DisplayName == "editor");

        Assert.Equal(new[] { 10 }, launcher.Processes.Select(p => p.Pid));
        Assert.Equal(new[] { 20, 21 }, editor.Processes.Select(p => p.Pid));
    }

    [Fact]
    public void HeadlessProcessLaunchedByExplorerIsNotPartOfExplorersStopTree()
    {
        var snapshot = new[]
        {
            Fixture.Process(4, 0, "explorer", @"C:\Windows\explorer.exe", hasMainWindow: true),
            Fixture.Process(10, 4, "helper", @"C:\Apps\helper.exe"),
        };

        var groups = Grouper().Group(snapshot, 1);
        var explorer = Assert.Single(groups, g => g.DisplayName == "explorer");
        var helper = Assert.Single(groups, g => g.DisplayName == "helper");

        Assert.Equal(new[] { 4 }, explorer.Processes.Select(p => p.Pid));
        Assert.Equal(new[] { 10 }, helper.Processes.Select(p => p.Pid));
    }

    [Fact]
    public void InteractiveAppInSystemDirectoryIsNotAssumedToBeACoreProcess()
    {
        var snapshot = new[]
        {
            Fixture.Process(10, 4, "SystemSettings", @"C:\Windows\System32\SystemSettings.exe", hasMainWindow: true),
        };

        var group = Assert.Single(Grouper().Group(snapshot, 1));
        Assert.Equal("SystemSettings", group.DisplayName);
    }
}
