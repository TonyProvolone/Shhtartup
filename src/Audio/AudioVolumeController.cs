using Shhtartup.Interop;

namespace Shhtartup;

internal static class AudioVolumeController
{
    // Give up on a process that still hasn't opened an audio session after this long.
    private const long GiveUpMs = 10 * 60_000;

    private sealed class PendingTarget
    {
        public uint Pid;
        public string? Path;
        public string Name = string.Empty;
        public string Source = string.Empty;
        public long StartedAt;
        public long NextProbeAt;
    }

    private static readonly List<PendingTarget> _pending = new();
    private static IMMDeviceEnumerator? _enumerator;

    // How often to look for a watched process's audio session, by time since it was detected: fast at
    // first to beat the startup "flashbang", then progressively slower so games with long splash
    // screens or anti-cheat startups are still caught. Watching also ends when the process exits.
    private static long ProbeIntervalMs(long ageMs) => ageMs switch
    {
        < 2_000 => 50,
        < 15_000 => 200,
        < 120_000 => 1_000,
        _ => 3_000,
    };

    public static void BeginWatching(uint pid, string? path, string name, string source)
    {
        if (_pending.Any(t => t.Pid == pid))
        {
            return;
        }

        var now = Environment.TickCount64;
        _pending.Add(new PendingTarget { Pid = pid, Path = path, Name = name, Source = source, StartedAt = now, NextProbeAt = now });
        Reschedule(now);
    }

    // Stop watching processes that have exited.
    public static void Prune(HashSet<uint> livePids)
    {
        if (_pending.RemoveAll(t => !livePids.Contains(t.Pid)) > 0)
        {
            Reschedule(Environment.TickCount64);
        }
    }

    public static void Tick()
    {
        var now = Environment.TickCount64;

        HashSet<uint>? duePids = null;
        foreach (var target in _pending)
        {
            if (target.NextProbeAt <= now)
            {
                (duePids ??= new()).Add(target.Pid);
            }
        }

        if (duePids is not null)
        {
            // One pass over every output device's sessions serves all due targets.
            var sessions = FindSessions(duePids);
            var level = Settings.Current.DefaultVolumePercent / 100f;
            var eventContext = Guid.Empty;

            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var target = _pending[i];
                if (target.NextProbeAt > now)
                {
                    continue;
                }

                if (sessions.TryGetValue(target.Pid, out var volumes))
                {
                    var clamped = false;
                    foreach (var volume in volumes)
                    {
                        clamped |= volume.SetMasterVolume(level, in eventContext) >= 0;
                    }

                    if (clamped)
                    {
                        HandledPidTracker.MarkHandled(target.Pid);
                        if (target.Path is not null)
                        {
                            KnownGames.Record(target.Path, target.Name, target.Source);
                        }
                        _pending.RemoveAt(i);
                        continue;
                    }
                }

                var age = now - target.StartedAt;
                if (age >= GiveUpMs)
                {
                    _pending.RemoveAt(i);
                    continue;
                }

                target.NextProbeAt = now + ProbeIntervalMs(age);
            }
        }

        Reschedule(now);
    }

    // Single timer, re-armed for whenever the next target is due; off entirely while nothing is pending.
    private static void Reschedule(long now)
    {
        if (_pending.Count == 0)
        {
            User32.KillTimer(TrayIcon.Hwnd, TimerIds.AudioPollTimer);
            return;
        }

        var nextDue = _pending.Min(t => t.NextProbeAt);
        var delay = (uint)Math.Clamp(nextDue - now, 10, 60_000);
        User32.SetTimer(TrayIcon.Hwnd, TimerIds.AudioPollTimer, delay, 0);
    }

    // Maps each wanted PID to its volume controls across every active output device, so games playing
    // through headphones/a second device are caught too, not only the Windows default device.
    private static Dictionary<uint, List<ISimpleAudioVolume>> FindSessions(HashSet<uint> wantedPids)
    {
        var result = new Dictionary<uint, List<ISimpleAudioVolume>>();

        var enumerator = GetEnumerator();
        if (enumerator is null)
        {
            return result;
        }

        if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, AudioGuids.DEVICE_STATE_ACTIVE, out var devices) < 0 || devices is null)
        {
            // The audio service may have restarted; recreate the enumerator next time.
            _enumerator = null;
            return result;
        }

        if (devices.GetCount(out var deviceCount) < 0)
        {
            return result;
        }

        for (uint d = 0; d < deviceCount; d++)
        {
            if (devices.Item(d, out var device) < 0 || device is null)
            {
                continue;
            }

            if (device.Activate(AudioGuids.IID_IAudioSessionManager2, Ole32.CLSCTX_INPROC_SERVER, 0, out var manager) < 0
                || manager is null)
            {
                continue;
            }

            if (manager.GetSessionEnumerator(out var sessions) < 0 || sessions is null || sessions.GetCount(out var count) < 0)
            {
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) < 0 || session is null)
                {
                    continue;
                }

                // S_OK (0) means this IS the system sounds session -- skip it.
                if (session.IsSystemSoundsSession() == 0)
                {
                    continue;
                }

                // Only exact S_OK identifies a single owning process; AUDCLNT_S_NO_SINGLE_PROCESS is a
                // success code too, but for a session shared by several processes.
                if (session.GetProcessId(out var sessionPid) != 0 || !wantedPids.Contains(sessionPid))
                {
                    continue;
                }

                if (session is ISimpleAudioVolume volume)
                {
                    if (!result.TryGetValue(sessionPid, out var list))
                    {
                        result[sessionPid] = list = new List<ISimpleAudioVolume>();
                    }
                    list.Add(volume);
                }
            }
        }

        return result;
    }

    private static IMMDeviceEnumerator? GetEnumerator()
    {
        if (_enumerator is null)
        {
            var hr = Ole32.CoCreateInstance(
                AudioGuids.CLSID_MMDeviceEnumerator, 0, Ole32.CLSCTX_INPROC_SERVER,
                AudioGuids.IID_IMMDeviceEnumerator, out IMMDeviceEnumerator enumerator);
            _enumerator = hr >= 0 ? enumerator : null;
        }
        return _enumerator;
    }
}
