using Tinnitdown;
using Tinnitdown.Interop;

// Crisp at any display scaling, and resized per monitor. Fails harmlessly if the manifest already set it.
User32.SetProcessDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

Ole32.CoInitializeEx(0, Ole32.COINIT_APARTMENTTHREADED);

var gdiplusInput = new GdiplusStartupInput { GdiplusVersion = 1 };
Gdiplus.GdiplusStartup(out var gdiplusToken, in gdiplusInput, 0);

LegacyMigration.Run();
Settings.Load();
KnownGames.Load();
Theme.Refresh();
TrayIcon.Initialize();

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

Gdiplus.GdiplusShutdown(gdiplusToken);
Ole32.CoUninitialize();
