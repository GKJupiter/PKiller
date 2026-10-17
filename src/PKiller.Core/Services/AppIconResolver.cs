using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>Finds an executable path from which the UI can try to load an app-group icon.</summary>
public sealed class AppIconResolver
{
    public string? GetExecutablePath(AppGroup group) =>
        group.Roots
            .Concat(group.Processes)
            .Select(process => process.ImagePath)
            .FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) &&
                string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase));
}
