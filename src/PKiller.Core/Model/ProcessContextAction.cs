namespace PKiller.Core.Model;

public enum ProcessContextActionKind
{
    OpenFileLocation,
    SearchOnline
}

public sealed record ProcessContextAction(
    ProcessContextActionKind Kind,
    string Title,
    string? ExecutablePath,
    string SearchQuery);
