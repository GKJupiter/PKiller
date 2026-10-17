using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>
/// Decides which processes belong in the app list. Only processes in the current signed-in session
/// are shown; Windows services and core system processes are excluded.
/// </summary>
public sealed class ProcessClassifier
{
    private static readonly HashSet<string> CoreSystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system",
        "registry",
        "smss",
        "csrss",
        "svchost",
        "services",
        "wininit",
        "winlogon",
        "lsass",
        "logonui",
        "fontdrvhost",
        "memory compression",
        "dwm",
        "taskhostw",
        "runtimebroker",
        "shellexperiencehost",
        "startmenuexperiencehost",
        "searchhost",
        "sihost",
        "ctfmon",
    };

    /// <summary>Known services are outside the interactive session. Unknown session IDs remain visible but unavailable.</summary>
    public bool IsService(ProcessInfo process, int currentSessionId) =>
        IsService(process.SessionId, currentSessionId);

    public bool IsService(int sessionId, int currentSessionId) =>
        sessionId >= 0 && sessionId != currentSessionId;

    public bool IsCoreSystemProcess(ProcessInfo process) => IsCoreSystemProcess(process.Name);

    public bool IsCoreSystemProcess(string name) => CoreSystemNames.Contains(name);

    public bool IsIncluded(ProcessInfo process, int currentSessionId) =>
        IsIncluded(process.Name, process.SessionId, currentSessionId);

    public bool IsIncluded(string name, int sessionId, int currentSessionId) =>
        !IsService(sessionId, currentSessionId) && !IsCoreSystemProcess(name);
}
