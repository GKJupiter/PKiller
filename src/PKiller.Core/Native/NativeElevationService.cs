using System.Diagnostics;
using System.Security.Principal;
using PKiller.Core.Abstractions;

namespace PKiller.Core.Native;

/// <summary>
/// PKiller never elevates on its own. This service only reports the current state, and relaunch is
/// called after the user explicitly chooses the administrator option.
/// </summary>
public sealed class NativeElevationService : IElevationService
{
    public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public bool CanRelaunchAsAdministrator => !IsElevated && Environment.ProcessPath is not null;

    public bool RelaunchAsAdministrator()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null)
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception)
        {
            // The user can decline the UAC prompt; the caller reports that nothing was elevated.
            return false;
        }
    }
}
