using System.Runtime.InteropServices;
using Tinnitdown.Interop;

namespace Tinnitdown;

internal static class TrayIcon
{
    private const string ClassName = "TinnitdownTrayWindowClass";
    private const uint WM_TRAY_CALLBACK = User32.WM_APP + 1;
    public const uint WM_UPDATE_CHECK_DONE = User32.WM_APP + 2;
    public const uint WM_UPDATE_PROGRESS = User32.WM_APP + 3; // wParam: percent, lParam: UpdateInstaller.Stage
    public const uint WM_UPDATE_INSTALL_DONE = User32.WM_APP + 4;
    public const uint WM_SHOW_SETTINGS = User32.WM_APP + 5; // From a second launch (SingleInstance).
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint TrayIconId = 1;

    public static nint Hwnd { get; private set; }

    private static uint _taskbarCreatedMessage;
    private static nint _hInstance;
    private static nint _hIcon;

    public static void Initialize()
    {
        _hInstance = User32.GetModuleHandleW(null);
        _hIcon = User32.LoadIconW(0, (nint)User32.IDI_APPLICATION);

        unsafe
        {
            var wndProcPtr = (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&WndProc;
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = 0,
                lpfnWndProc = wndProcPtr,
                hInstance = _hInstance,
                lpszClassName = ClassName,
            };
            User32.RegisterClassExW(in wndClass);
        }

        Hwnd = User32.CreateWindowExW(
            0, ClassName, "Tinnitdown", 0,
            0, 0, 0, 0,
            User32.HWND_MESSAGE, 0, _hInstance, 0);

        _taskbarCreatedMessage = User32.RegisterWindowMessageW("TaskbarCreated");

        AddTrayIcon();
    }

    private static void AddTrayIcon()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = Hwnd,
            uID = TrayIconId,
            uFlags = Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP | Shell32.NIF_SHOWTIP,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = _hIcon,
            szTip = TooltipText(),
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        Shell32.Shell_NotifyIconW(Shell32.NIM_ADD, ref data);

        data.uVersion = Shell32.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIconW(Shell32.NIM_SETVERSION, ref data);
    }

    private static string TooltipText() => $"Tinnitdown: {Settings.Current.DefaultVolumePercent}%";

    // Refresh the hover tooltip after a volume change. Called from both UIs via Settings changes.
    public static void UpdateTooltip()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = Hwnd,
            uID = TrayIconId,
            uFlags = Shell32.NIF_TIP | Shell32.NIF_SHOWTIP,
            szTip = TooltipText(),
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
        Shell32.Shell_NotifyIconW(Shell32.NIM_MODIFY, ref data);
    }

    // Windows notification attached to the tray icon (a toast on Windows 10/11).
    public static void ShowNotification(string title, string text, bool warning)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = Hwnd,
            uID = TrayIconId,
            uFlags = Shell32.NIF_INFO,
            szTip = string.Empty,
            szInfo = text,
            szInfoTitle = title,
            dwInfoFlags = warning ? Shell32.NIIF_WARNING : Shell32.NIIF_INFO,
        };
        Shell32.Shell_NotifyIconW(Shell32.NIM_MODIFY, ref data);
    }

    public static void ExitApp()
    {
        RemoveTrayIcon();
        User32.PostQuitMessage(0);
    }

    private static void RemoveTrayIcon()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = Hwnd,
            uID = TrayIconId,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
        Shell32.Shell_NotifyIconW(Shell32.NIM_DELETE, ref data);
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == WM_TRAY_CALLBACK)
        {
            var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == User32.WM_RBUTTONUP || mouseMsg == WM_CONTEXTMENU)
            {
                ShowContextMenu();
            }
            else if (mouseMsg == Shell32.NIN_BALLOONUSERCLICK)
            {
                UpdateChecker.OnNotificationClicked();
            }
            return 0;
        }

        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            AddTrayIcon();
            return 0;
        }

        switch (msg)
        {
            case User32.WM_TIMER:
                if (wParam == TimerIds.ScanTimer)
                {
                    ProcessWatcher.Tick();
                }
                else if (wParam == TimerIds.AudioPollTimer)
                {
                    AudioVolumeController.Tick();
                }
                else if (wParam == TimerIds.UpdateCheckTimer)
                {
                    UpdateChecker.OnStartupTimer();
                }
                return 0;

            case WM_UPDATE_CHECK_DONE:
                UpdateChecker.OnCheckDone();
                return 0;

            case WM_UPDATE_PROGRESS:
                UpdateToast.SetProgress((UpdateInstaller.Stage)lParam, (int)wParam);
                return 0;

            case WM_UPDATE_INSTALL_DONE:
                UpdateInstaller.OnInstallDone();
                return 0;

            case WM_SHOW_SETTINGS:
                SettingsWindow.Show();
                return 0;

            // Sent by the installer/uninstaller (SingleInstance.CloseRunning) to free the exe.
            case User32.WM_CLOSE:
                ExitApp();
                return 0;

            case User32.WM_DESTROY:
                User32.PostQuitMessage(0);
                return 0;
        }

        return User32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void ShowContextMenu()
    {
        QuickMenu.Show();
    }
}
