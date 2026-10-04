using Tinnitdown.Interop;

namespace Tinnitdown;

internal static class Program
{
    // [STAThread] makes the main thread a single-threaded COM apartment, which the shell's folder
    // picker requires (it hangs otherwise). Without it .NET makes the thread MTA before Main runs and
    // the CoInitializeEx call below fails with RPC_E_CHANGED_MODE.
    [STAThread]
    private static void Main(string[] args)
    {
        // Crisp at any display scaling, and resized per monitor. Fails harmlessly if the manifest already set it.
        User32.SetProcessDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        Ole32.CoInitializeEx(0, Ole32.COINIT_APARTMENTTHREADED);

        var gdiplusInput = new GdiplusStartupInput { GdiplusVersion = 1 };
        Gdiplus.GdiplusStartup(out var gdiplusToken, in gdiplusInput, 0);

        Run(args);

        Gdiplus.GdiplusShutdown(gdiplusToken);
        Ole32.CoUninitialize();
    }

    private static void Run(string[] args)
    {
        if (args.Contains(Installer.UninstallArg))
        {
            Installer.Uninstall();
            return;
        }

#if !DEBUG
        // Debug builds run in place, so development doesn't keep installing over the real copy.
        if (!Installer.RunSetupIfNeeded())
        {
            return;
        }
#endif

        // Just installed or updated: the previous copy may still be on its way out, so wait for it.
        var afterInstall = args.Contains(Installer.AfterInstallArg);
        var afterUpdate = args.Contains(UpdateInstaller.AfterUpdateArg);
        if (!SingleInstance.TryAcquire(afterInstall || afterUpdate ? TimeSpan.FromSeconds(10) : TimeSpan.Zero))
        {
            SingleInstance.ShowRunningSettings();
            return;
        }

        LegacyMigration.Run();
        Settings.Load();
        KnownGames.Load();
        Theme.Refresh();
        TrayIcon.Initialize();

        if (afterInstall)
        {
            TrayIcon.ShowNotification("Installation successful",
                "Tinnitdown is now running in the system tray. Right-click to change the volume or adjust settings.", warning: false);
        }
        else if (afterUpdate)
        {
            TrayIcon.ShowNotification("Tinnitdown updated", $"Now on version {UpdateChecker.CurrentVersion}.", warning: false);
        }

        User32.SetTimer(TrayIcon.Hwnd, TimerIds.ScanTimer, 300, 0);
#if !DEBUG
        // Debug builds skip this so a dev build isn't offered (and replaced by) the latest release.
        User32.SetTimer(TrayIcon.Hwnd, TimerIds.UpdateCheckTimer, UpdateChecker.StartupDelayMs, 0);
#endif

        while (User32.GetMessageW(out var msg, 0, 0, 0) != 0)
        {
            if (SettingsWindow.TryHandleKey(in msg))
            {
                continue;
            }

            if (SettingsWindow.Hwnd != 0 && User32.IsDialogMessageW(SettingsWindow.Hwnd, in msg))
            {
                continue;
            }

            User32.TranslateMessage(in msg);
            User32.DispatchMessageW(in msg);
        }
    }
}
