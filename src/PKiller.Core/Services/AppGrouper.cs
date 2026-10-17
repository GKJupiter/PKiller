using PKiller.Core.Model;

namespace PKiller.Core.Services;

/// <summary>
/// Groups processes by app. Each group is the process tree rooted at an app's top-level process;
/// trees that share the same image path are merged into one app row.
/// </summary>
public sealed class AppGrouper
{
    private readonly ProcessClassifier _classifier;
    private readonly MetricsAggregator _metrics;

    public AppGrouper(ProcessClassifier classifier, MetricsAggregator metrics)
    {
        _classifier = classifier;
        _metrics = metrics;
    }

    public IReadOnlyList<AppGroup> Group(IEnumerable<ProcessInfo> snapshot, int currentSessionId)
    {
        var included = snapshot.Where(p => _classifier.IsIncluded(p, currentSessionId)).ToList();

        var byPid = new Dictionary<int, ProcessInfo>(included.Count);
        var groupKeyByPid = new Dictionary<int, string>(included.Count);
        foreach (var process in included)
        {
            // A snapshot cannot contain the same PID twice; keep the first occurrence defensively.
            if (byPid.TryAdd(process.Pid, process))
            {
                groupKeyByPid.Add(process.Pid, process.GroupKey);
            }
        }

        var children = new Dictionary<int, List<ProcessInfo>>(included.Count);
        var roots = new List<ProcessInfo>(included.Count);
        var cycleCache = new Dictionary<int, bool>(included.Count);
        var cyclePath = new List<int>();
        var cycleSeen = new HashSet<int>();

        foreach (var process in included)
        {
            if (IsRoot(process, byPid, groupKeyByPid, cycleCache, cyclePath, cycleSeen))
            {
                roots.Add(process);
            }
            else
            {
                if (!children.TryGetValue(process.ParentPid, out var list))
                {
                    list = new List<ProcessInfo>();
                    children[process.ParentPid] = list;
                }

                list.Add(process);
            }
        }
        var rootPids = new HashSet<int>(roots.Count);
        foreach (var root in roots)
        {
            rootPids.Add(root.Pid);
        }

        var groups = new Dictionary<string, List<ProcessInfo>>(roots.Count, StringComparer.Ordinal);
        var groupRoots = new Dictionary<string, List<ProcessInfo>>(roots.Count, StringComparer.Ordinal);

        foreach (var root in roots)
        {
            var key = GroupKeyFor(root, byPid, groupKeyByPid);
            if (!groups.TryGetValue(key, out var members))
            {
                members = new List<ProcessInfo>();
                groups[key] = members;
                groupRoots[key] = new List<ProcessInfo>();
            }

            groupRoots[key].Add(root);
            members.AddRange(DepthFirst(root, children, rootPids));
        }

        return groups
            .Select(entry => _metrics.Build(
                entry.Key,
                DisplayNameFor(groupRoots[entry.Key][0]),
                entry.Value,
                groupRoots[entry.Key]))
            .ToList();
    }

    /// <summary>A process is a group root when its parent is outside the app list (or unreachable).</summary>
    private static bool IsRoot(
        ProcessInfo process,
        Dictionary<int, ProcessInfo> byPid,
        Dictionary<int, string> groupKeyByPid,
        Dictionary<int, bool> cycleCache,
        List<int> cyclePath,
        HashSet<int> cycleSeen)
    {
        if (!byPid.TryGetValue(process.ParentPid, out var parent) || ReferenceEquals(parent, process))
        {
            return true;
        }

        if (!string.Equals(
                GroupKeyFor(process, byPid, groupKeyByPid),
                GroupKeyFor(parent, byPid, groupKeyByPid),
                StringComparison.OrdinalIgnoreCase) &&
            (process.HasMainWindow || string.Equals(parent.Name, "explorer", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!ReferenceEquals(byPid[process.Pid], process))
        {
            return HasCyclicParentChainWithoutCache(process, byPid);
        }

        return HasCyclicParentChain(process, byPid, cycleCache, cyclePath, cycleSeen);
    }

    private static string GroupKeyFor(
        ProcessInfo process,
        Dictionary<int, ProcessInfo> byPid,
        Dictionary<int, string> groupKeyByPid) =>
        byPid.TryGetValue(process.Pid, out var indexedProcess) && ReferenceEquals(indexedProcess, process)
            ? groupKeyByPid[process.Pid]
            : process.GroupKey;

    private static bool HasCyclicParentChainWithoutCache(
        ProcessInfo process,
        Dictionary<int, ProcessInfo> byPid)
    {
        var seen = new HashSet<int> { process.Pid };
        var cursor = byPid[process.ParentPid];
        while (true)
        {
            if (!seen.Add(cursor.Pid))
            {
                return true;
            }

            if (!byPid.TryGetValue(cursor.ParentPid, out cursor))
            {
                return false;
            }
        }
    }

    private static bool HasCyclicParentChain(
        ProcessInfo process,
        Dictionary<int, ProcessInfo> byPid,
        Dictionary<int, bool> cycleCache,
        List<int> cyclePath,
        HashSet<int> cycleSeen)
    {
        cyclePath.Clear();
        cycleSeen.Clear();
        var cursor = process;
        bool reachesCycle;

        while (true)
        {
            if (cycleCache.TryGetValue(cursor.Pid, out reachesCycle))
            {
                break;
            }

            if (!cycleSeen.Add(cursor.Pid))
            {
                reachesCycle = true;
                break;
            }

            cyclePath.Add(cursor.Pid);
            if (!byPid.TryGetValue(cursor.ParentPid, out cursor))
            {
                reachesCycle = false;
                break;
            }
        }

        foreach (int pid in cyclePath)
        {
            cycleCache[pid] = reachesCycle;
        }

        return reachesCycle;
    }

    private static IEnumerable<ProcessInfo> DepthFirst(
        ProcessInfo root,
        Dictionary<int, List<ProcessInfo>> children,
        HashSet<int> rootPids)
    {
        var visited = new HashSet<int>();
        var stack = new Stack<ProcessInfo>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.Pid != root.Pid && rootPids.Contains(current.Pid))
            {
                continue;
            }

            if (!visited.Add(current.Pid))
            {
                continue;
            }

            yield return current;

            if (children.TryGetValue(current.Pid, out var kids))
            {
                // Push in reverse so children come out in creation order.
                for (var i = kids.Count - 1; i >= 0; i--)
                {
                    stack.Push(kids[i]);
                }
            }
        }
    }

    private static string DisplayNameFor(ProcessInfo root)
    {
        var source = root.ImagePath is null ? root.Name : Path.GetFileNameWithoutExtension(root.ImagePath);
        return string.IsNullOrWhiteSpace(source) ? root.Name : source;
    }
}
