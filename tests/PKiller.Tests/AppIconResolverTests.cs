using PKiller.Core.Model;
using PKiller.Core.Services;

namespace PKiller.Tests;

public sealed class AppIconResolverTests
{
    [Fact]
    public void UsesExecutablePathFromAppRootWhenAvailable()
    {
        var root = Fixture.Process(10, 4, "app", @"C:\Apps\app.exe", hasMainWindow: true);
        var group = Group(root);

        Assert.Equal(root.ImagePath, new AppIconResolver().GetExecutablePath(group));
    }

    [Fact]
    public void FallsBackWhenNoExecutablePathCanBeResolved()
    {
        var root = Fixture.Process(10, 4, "app", imagePath: null, hasMainWindow: true);
        var group = Group(root);

        Assert.Null(new AppIconResolver().GetExecutablePath(group));
    }

    [Fact]
    public void UsesAnotherAppProcessPathWhenRootPathIsUnavailable()
    {
        var root = Fixture.Process(10, 4, "app", imagePath: null, hasMainWindow: true);
        var child = Fixture.Process(11, 10, "app", @"C:\Apps\app.exe");
        var group = Group(root, child);

        Assert.Equal(child.ImagePath, new AppIconResolver().GetExecutablePath(group));
    }

    [Fact]
    public void IgnoresNonExecutablePaths()
    {
        var root = Fixture.Process(10, 4, "app", @"C:\Apps\app.dll", hasMainWindow: true);
        var group = Group(root);

        Assert.Null(new AppIconResolver().GetExecutablePath(group));
    }

    private static AppGroup Group(params ProcessInfo[] processes) =>
        new()
        {
            Key = "app",
            DisplayName = "app",
            Roots = new[] { processes[0] },
            Processes = processes,
        };
}
