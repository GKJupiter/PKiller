using PKiller.Core.Model;
using PKiller.Core.Services;

namespace PKiller.Tests;

public sealed class ProcessContextActionFactoryTests
{
    private readonly ProcessContextActionFactory _factory = new();

    [Fact]
    public void AppRowContextMenuHasExactlyTheRequiredActions()
    {
        var process = Fixture.Process(10, 4, "editor", @"C:\Apps\editor.exe", hasMainWindow: true);
        var actions = _factory.ForApp(Group(process));

        Assert.Equal(
            new[] { "Open file location", "Search online" },
            actions.Select(action => action.Title));
        Assert.All(actions, action => Assert.Equal(process.ImagePath, action.ExecutablePath));
        Assert.All(actions, action => Assert.Equal("editor", action.SearchQuery));
    }

    [Fact]
    public void ChildProcessContextMenuUsesTheChildExecutableAndName()
    {
        var process = Fixture.Process(11, 10, "worker", @"C:\Apps\worker.exe");
        var actions = _factory.ForProcess(process);

        Assert.Equal(
            new[] { "Open file location", "Search online" },
            actions.Select(action => action.Title));
        Assert.All(actions, action => Assert.Equal(process.ImagePath, action.ExecutablePath));
        Assert.All(actions, action => Assert.Equal("worker", action.SearchQuery));
    }

    [Fact]
    public void MissingExecutablePathDoesNotRemoveEitherContextAction()
    {
        var process = Fixture.Process(11, 10, "worker", imagePath: null);
        var actions = _factory.ForProcess(process);

        Assert.Equal(2, actions.Count);
        Assert.Null(actions[0].ExecutablePath);
        Assert.Equal(ProcessContextActionKind.OpenFileLocation, actions[0].Kind);
        Assert.Equal(ProcessContextActionKind.SearchOnline, actions[1].Kind);
    }

    private static AppGroup Group(ProcessInfo process) =>
        new()
        {
            Key = process.GroupKey,
            DisplayName = process.Name,
            Roots = new[] { process },
            Processes = new[] { process },
        };
}
