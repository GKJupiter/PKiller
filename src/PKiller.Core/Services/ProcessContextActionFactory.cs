using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>Defines the exact two context-menu actions offered on app and process rows.</summary>
public sealed class ProcessContextActionFactory
{
    public IReadOnlyList<ProcessContextAction> ForApp(AppGroup group) =>
        Create(new AppIconResolver().GetExecutablePath(group), group.DisplayName);

    public IReadOnlyList<ProcessContextAction> ForProcess(ProcessInfo process) =>
        Create(process.ImagePath, process.Name);

    private static IReadOnlyList<ProcessContextAction> Create(string? executablePath, string name) =>
        new[]
        {
            new ProcessContextAction(
                ProcessContextActionKind.OpenFileLocation,
                "Open file location",
                executablePath,
                name),
            new ProcessContextAction(
                ProcessContextActionKind.SearchOnline,
                "Search online",
                executablePath,
                name),
        };
}
