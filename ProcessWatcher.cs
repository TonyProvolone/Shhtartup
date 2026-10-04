using Tinnitdown.Interop;

namespace Tinnitdown;

internal static class ProcessWatcher
{
    private const int MaxAncestorHops = 8;

    // How long a launcher-descendant PID is remembered after it exits. Covers launchers that start a
    // game through a short-lived bootstrapper which exits before (or right after) the game appears.
    private const long DescendantMemoryMs = 60_000;

    private const long LibraryRefreshMs = 10 * 60_000;

    private static HashSet<uint> _previousPids = new();
    private static bool _initialized;
    private static long _nextLibraryRefresh;

    // PID -> last time (Environment.TickCount64) it was seen alive, for every process known to
    // descend from a launcher. Entries for exited processes are kept for DescendantMemoryMs.
    private static readonly Dictionary<uint, long> _launcherDescendants = new();

    public static void Tick()
    {
        var now = Environment.TickCount64;
        if (now >= _nextLibraryRefresh)
        {
            // Picks up games installed while Tinnitdown is running.
            GameLibraries.Refresh();
            _nextLibraryRefresh = now + LibraryRefreshMs;
        }

        var snapshot = TakeSnapshot();
        if (snapshot is null)
        {
            return;
        }

        var currentPids = new HashSet<uint>(snapshot.Keys);

        if (!_initialized)
        {
            // Baseline run: learn the existing launcher process trees, but don't touch the volume of
            // anything that was already running before Tinnitdown started.
            foreach (var pid in currentPids)
            {
                if (IsLauncherDescendant(pid, snapshot))
                {
                    _launcherDescendants[pid] = now;
                }
            }

            _previousPids = currentPids;
            _initialized = true;
            HandledPidTracker.Prune(currentPids);
            return;
        }

        foreach (var pid in currentPids)
        {
            if (_previousPids.Contains(pid))
            {
                continue;
            }

            // A new PID may be a reused one, so its launcher-descendant status is always recomputed.
            var fromLauncher = IsLauncherDescendant(pid, snapshot);
            if (fromLauncher)
            {
                _launcherDescendants[pid] = now;
            }
            else
            {
                _launcherDescendants.Remove(pid);
            }

            var exeName = snapshot[pid].ExeName;
            if (HandledPidTracker.IsHandled(pid) || LauncherCatalog.IsLauncherOrExcluded(exeName))
            {
                continue;
            }

            Classify(pid, exeName, fromLauncher);
        }

        UpdateDescendantMemory(currentPids, now);

        _previousPids = currentPids;
        HandledPidTracker.Prune(currentPids);
        AudioVolumeController.Prune(currentPids);
    }

    // Decides whether a new process is a game, in order of confidence:
    //   1. Its exe was clamped before (remembered) -- catches it however it was launched.
    //   2. It was started by a game launcher.
    //   3. It's installed in a game launcher's library folder.
    private static void Classify(uint pid, string exeName, bool fromLauncher)
    {
        var path = Kernel32.GetProcessImagePath(pid);
        if (path is not null && GameLibraries.IsSystemPath(path))
        {
            return;
        }

        string? source = null;
        if (path is not null && KnownGames.Contains(path))
        {
            source = "remembered";
        }
        else if (fromLauncher)
        {
            source = "launcher";
        }
        else if (path is not null)
        {
            source = GameLibraries.Classify(path);
        }

        if (source is not null)
        {
            AudioVolumeController.BeginWatching(pid, path, exeName, source);
        }
    }

    private static bool IsLauncherDescendant(uint pid, Dictionary<uint, (uint ParentPid, string ExeName)> snapshot)
    {
        var currentPid = pid;
        for (var hop = 0; hop < MaxAncestorHops; hop++)
        {
            if (!snapshot.TryGetValue(currentPid, out var entry))
            {
                // The ancestor has exited; trust what we learned about it while it was alive.
                return hop > 0 && _launcherDescendants.ContainsKey(currentPid);
            }

            if (LauncherCatalog.LauncherExeNames.Contains(entry.ExeName))
            {
                return true;
            }

            if (hop > 0 && _launcherDescendants.ContainsKey(currentPid))
            {
                return true;
            }

            if (entry.ParentPid == 0 || entry.ParentPid == currentPid)
            {
                return false;
            }

            currentPid = entry.ParentPid;
        }

        return false;
    }

    private static void UpdateDescendantMemory(HashSet<uint> currentPids, long now)
    {
        foreach (var pid in currentPids)
        {
            if (_launcherDescendants.ContainsKey(pid))
            {
                _launcherDescendants[pid] = now;
            }
        }

        List<uint>? expired = null;
        foreach (var (pid, lastSeen) in _launcherDescendants)
        {
            if (now - lastSeen > DescendantMemoryMs)
            {
                (expired ??= new()).Add(pid);
            }
        }

        if (expired is not null)
        {
            foreach (var pid in expired)
            {
                _launcherDescendants.Remove(pid);
            }
        }
    }

    private static Dictionary<uint, (uint ParentPid, string ExeName)>? TakeSnapshot()
    {
        var handle = Kernel32.CreateToolhelp32Snapshot(Kernel32.TH32CS_SNAPPROCESS, 0);
        if (handle == Kernel32.INVALID_HANDLE_VALUE)
        {
            return null;
        }

        try
        {
            var result = new Dictionary<uint, (uint ParentPid, string ExeName)>();
            var entry = new PROCESSENTRY32W { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32W>() };

            if (!Kernel32.Process32FirstW(handle, ref entry))
            {
                return result;
            }

            do
            {
                result[entry.th32ProcessID] = (entry.th32ParentProcessID, entry.szExeFile);
                entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32W>();
            }
            while (Kernel32.Process32NextW(handle, ref entry));

            return result;
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }
}
