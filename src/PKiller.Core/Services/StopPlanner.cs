using System.Text;
using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>
/// Decides which action an app row offers and what the user must confirm before it runs.
/// </summary>
public sealed class StopPlanner
{
    /// <summary>Apps with no closeable window offer force-stop immediately.</summary>
    public StopAction ActionFor(AppGroup group) => group.HasCloseableWindow ? StopAction.GracefulClose : StopAction.ForceStop;

    /// <summary>
    /// A single graceful close needs no confirmation. Batch graceful close and any force-stop do.
    /// </summary>
    public bool RequiresConfirmation(IReadOnlyList<AppStopTarget> targets, StopAction action)
    {
        if (targets.Count == 0)
        {
            return false;
        }

        return action == StopAction.ForceStop || targets.Count > 1;
    }

    /// <summary>Names the exact apps affected and always states the unsaved-work risk for force-stop.</summary>
    public string ConfirmationMessage(IReadOnlyList<AppStopTarget> targets, StopAction action)
    {
        var builder = new StringBuilder();

        if (action == StopAction.ForceStop)
        {
            builder.Append(targets.Count == 1
                ? $"Force stop {targets[0].DisplayName}?"
                : $"Force stop {targets.Count} apps?");
            builder.Append(" This will end the process tree immediately and may lose unsaved work.");
        }
        else
        {
            builder.Append(targets.Count == 1
                ? $"Close {targets[0].DisplayName}?"
                : $"Close {targets.Count} apps?");
            builder.Append(" Each app is asked to close normally.");
        }

        builder.AppendLine();
        builder.Append("Apps affected: ");
        builder.Append(string.Join(", ", targets.Select(t => t.DisplayName)));

        return builder.ToString();
    }
}
