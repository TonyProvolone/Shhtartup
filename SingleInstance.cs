using System.Diagnostics;
using Shhtartup.Interop;

namespace Shhtartup;

// One tray app per user session. A second launch just opens the running copy's Settings window.
// The installer and updater also use this to close the running copy before replacing its exe.
internal static class SingleInstance
{
    private const string MutexName = @"Local\Shhtartup.Instance";
    private const string TrayClassName = "ShhtartupTrayWindowClass";

    private static Mutex? _mutex;

    // wait: how long to wait for a copy that's on its way out (a restart after updating or installing).
    public static bool TryAcquire(TimeSpan wait)
    {
        _mutex = new Mutex(false, MutexName);
        try
        {
            return _mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // The previous copy exited without releasing it -- it's ours now.
            return true;
        }
    }

    public static void ShowRunningSettings()
    {
        var hwnd = User32.FindWindowExW(User32.HWND_MESSAGE, 0, TrayClassName, null);
        if (hwnd != 0)
        {
            User32.PostMessageW(hwnd, TrayIcon.WM_SHOW_SETTINGS, 0, 0);
        }
    }

    // Asks every running copy (including versions from before the single-instance lock) to exit,
    // and waits for them to go so their exe can be replaced or deleted.
    public static void CloseRunning(TimeSpan timeout)
    {
        var self = (uint)Environment.ProcessId;
        var processes = new List<Process>();

        for (var hwnd = User32.FindWindowExW(User32.HWND_MESSAGE, 0, TrayClassName, null);
             hwnd != 0;
             hwnd = User32.FindWindowExW(User32.HWND_MESSAGE, hwnd, TrayClassName, null))
        {
            User32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == self)
            {
                continue;
            }

            try
            {
                processes.Add(Process.GetProcessById((int)pid));
            }
            catch (ArgumentException)
            {
                continue; // Already gone.
            }
            User32.PostMessageW(hwnd, User32.WM_CLOSE, 0, 0);
        }

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        foreach (var process in processes)
        {
            using (process)
            {
                var left = (int)Math.Max(0, deadline - Environment.TickCount64);
                if (!process.WaitForExit(left))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                    catch (Exception)
                    {
                        // Exited in the meantime, or not ours to kill.
                    }
                }
            }
        }
    }
}
