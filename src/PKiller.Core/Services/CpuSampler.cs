using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>
/// Computes CPU usage normalised to total machine capacity. A single logical processor at full tilt
/// reports 100 / logicalProcessorCount, so the whole machine busy is 100%.
/// </summary>
public sealed class CpuSampler
{
    private const long TicksPerSecond = 10_000_000; // Windows process CPU times are in 100 ns units.

    private readonly int _logicalProcessorCount;
    private Dictionary<ProcessIdentity, (long Kernel, long User)> _previous = new();
    private Dictionary<ProcessIdentity, (long Kernel, long User)> _next = new();

    public CpuSampler(int logicalProcessorCount)
    {
        _logicalProcessorCount = Math.Max(1, logicalProcessorCount);
    }

    public int LogicalProcessorCount => _logicalProcessorCount;

    /// <summary>
    /// Sample a snapshot against the previous one. <paramref name="elapsedSeconds"/> is the wall time
    /// between the two samples. First-seen processes report 0 because there is no baseline.
    /// </summary>
    public IReadOnlyList<ProcessInfo> Sample(IReadOnlyList<ProcessInfo> snapshot, double elapsedSeconds)
    {
        _next.Clear();
        _next.EnsureCapacity(snapshot.Count);
        var result = new List<ProcessInfo>(snapshot.Count);

        foreach (var process in snapshot)
        {
            ProcessIdentity identity = process.Identity;
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0 ||
                !_previous.TryGetValue(identity, out var previous))
            {
                _next[identity] = (process.KernelTimeTicks, process.UserTimeTicks);
                result.Add(process with { CpuPercent = 0 });
                continue;
            }

            // A process that restarted under the same identity cannot happen (identity includes start time),
            // but counters can still look negative after a wrap, so clamp.
            long deltaTicks = (process.KernelTimeTicks - previous.Kernel) + (process.UserTimeTicks - previous.User);
            if (deltaTicks < 0)
            {
                deltaTicks = 0;
            }

            double busySeconds = deltaTicks / (double)TicksPerSecond;
            double capacitySeconds = elapsedSeconds * _logicalProcessorCount;
            double percent = capacitySeconds <= 0 || !double.IsFinite(busySeconds)
                ? 0
                : Math.Clamp((busySeconds / capacitySeconds) * 100.0, 0, 100);

            _next[identity] = (process.KernelTimeTicks, process.UserTimeTicks);
            result.Add(process with { CpuPercent = percent });
        }

        (_previous, _next) = (_next, _previous);
        return result;
    }

    /// <summary>Drop the baseline so a pause/resume does not produce a spike from stale data.</summary>
    public void Reset() => _previous.Clear();
}
