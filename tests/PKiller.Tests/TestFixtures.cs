using PKiller.Core.Abstractions;
using PKiller.Core.Model;

namespace PKiller.Tests;

internal sealed class FakeSnapshotSource : IProcessSnapshotSource
{
    private List<ProcessInfo> _processes = new();

    public int CurrentSessionId { get; init; } = 1;

    public void Set(IEnumerable<ProcessInfo> processes) => _processes = processes.ToList();

    /// <summary>Simulate a PID being reused by a different process instance.</summary>
    public void ReusePid(int pid, ProcessInfo replacement)
    {
        _processes.RemoveAll(p => p.Pid == pid);
        _processes.Add(replacement);
    }

    public void Remove(int pid) => _processes.RemoveAll(p => p.Pid == pid);

    public IReadOnlyList<ProcessInfo> Read() => _processes;

    public ProcessInfo? Read(int pid) => _processes.FirstOrDefault(p => p.Pid == pid);
}

internal sealed class FakeResolver : IProcessResolver
{
    private readonly FakeSnapshotSource _source;

    public FakeResolver(FakeSnapshotSource source) => _source = source;

    public Resolution Resolve(ProcessIdentity identity)
    {
        var current = _source.Read().FirstOrDefault(p => p.Pid == identity.Pid);
        if (current is null)
        {
            return Resolution.Exited(identity);
        }

        if (current.StartTime == DateTimeOffset.MinValue)
        {
            return new Resolution(identity, ResolutionStatus.Unverifiable, current);
        }

        return current.Identity.SameInstance(identity)
            ? Resolution.Running(identity, current)
            : Resolution.Stale(identity, current);
    }
}

internal sealed class FakeWindowCloser : IWindowCloser
{
    public List<ProcessIdentity> Closed { get; } = new();
    public Func<ProcessIdentity, CloseResult> Behavior { get; set; } = _ => CloseResult.Ok("Close request sent.");

    public CloseResult Close(ProcessIdentity identity)
    {
        Closed.Add(identity);
        return Behavior(identity);
    }
}

internal sealed class FakeTerminator : IProcessTerminator
{
    public List<ProcessIdentity> Terminated { get; } = new();
    public Func<ProcessIdentity, TerminateResult> Behavior { get; set; } = _ => TerminateResult.Ok("Terminated.");

    public TerminateResult Terminate(ProcessIdentity identity)
    {
        Terminated.Add(identity);
        return Behavior(identity);
    }
}

internal sealed class FakeClock : IClock
{
    public List<TimeSpan> Delays { get; } = new();

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}

internal static class Fixture
{
    public static ProcessInfo Process(
        int pid,
        int parentPid,
        string name,
        string? imagePath = null,
        int sessionId = 1,
        DateTimeOffset? startTime = null,
        long workingSetBytes = 0,
        double cpuPercent = 0,
        long kernelTimeTicks = 0,
        long userTimeTicks = 0,
        long readBytes = 0,
        long writeBytes = 0,
        bool hasMainWindow = false,
        bool isAccessible = true) =>
        new()
        {
            Pid = pid,
            ParentPid = parentPid,
            Name = name,
            ImagePath = imagePath,
            SessionId = sessionId,
            StartTime = startTime ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(pid),
            WorkingSetBytes = workingSetBytes,
            CpuPercent = cpuPercent,
            KernelTimeTicks = kernelTimeTicks,
            UserTimeTicks = userTimeTicks,
            ReadBytes = readBytes,
            WriteBytes = writeBytes,
            HasMainWindow = hasMainWindow,
            IsAccessible = isAccessible,
            CanTerminate = isAccessible,
        };
}
