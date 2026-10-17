using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>Aggregates app-level metrics over the processes included in a group.</summary>
public sealed class MetricsAggregator
{
    public AppGroup Build(string key, string displayName, IReadOnlyList<ProcessInfo> processes, IReadOnlyList<ProcessInfo> roots)
    {
        double cpuPercent = 0;
        long workingSetBytes = 0;
        long readBytes = 0;
        long writeBytes = 0;
        bool hasCloseableWindow = false;
        bool isAccessible = true;
        bool canTerminate = true;

        foreach (ProcessInfo process in processes)
        {
            cpuPercent += process.CpuPercent;
            workingSetBytes = checked(workingSetBytes + process.WorkingSetBytes);
            readBytes = checked(readBytes + process.ReadBytes);
            writeBytes = checked(writeBytes + process.WriteBytes);
            hasCloseableWindow |= process.HasMainWindow;
            isAccessible &= process.IsAccessible;
            canTerminate &= process.CanTerminate;
        }

        return new AppGroup
        {
            Key = key,
            DisplayName = displayName,
            Roots = roots,
            Processes = processes,
            CpuPercent = Math.Clamp(cpuPercent, 0, 100),
            WorkingSetBytes = workingSetBytes,
            ReadBytes = readBytes,
            WriteBytes = writeBytes,
            HasCloseableWindow = hasCloseableWindow,
            IsAccessible = isAccessible,
            CanTerminate = canTerminate,
        };
    }

    public static double WorkingSetMb(long bytes) => bytes / 1024.0 / 1024.0;
}
