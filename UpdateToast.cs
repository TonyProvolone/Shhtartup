using System.Runtime.InteropServices;
using Tinnitdown.Interop;

namespace Tinnitdown;

// Small Windows 11-style card in the bottom-right corner offering an available update. Tray
// notifications can't host buttons, so like QuickMenu this is a custom-drawn popup. It never takes
// focus (WS_EX_NOACTIVATE), and when the offer comes from the startup check it waits until no
// fullscreen app or presentation is running before appearing.
internal static class UpdateToast
{
    private const string ClassName = "TinnitdownUpdateToastClass";

    // Layout in device-independent pixels.
    private const int WidthDip = 360;
    private const int OfferHeightDip = 198;
    private const int ProgressHeightDip = 96;
    private const int PadDip = 16;
    private const int IconBoxDip = 20;
    private const int TextXDip = 46;
    private const int TitleYDip = 14;
    private const int LineHDip = 20;
    private const int SubtitleYDip = 36;
    private const int ButtonsYDip = 70;
    private const int ButtonHDip = 32;
    private const int ButtonGapDip = 8;
    private const int ProgressYDip = 72;
    private const int ProgressHDip = 4;
    private const int MarginDip = 12;

    private const uint BusyRetryMs = 60_000;

    private enum Mode { Offer, Progress }
    private enum Part { None, InstallNow, InstallLater, RemindLater }

    private static nint _hwnd;
    private static UiFonts? _fonts;
    private static Mode _mode;
    private static ReleaseInfo? _release;
    private static ReleaseInfo? _deferred;
    private static Version? _installingVersion;
    private static UpdateInstaller.Stage _stage;
    private static bool _willRestart;
    private static int _percent;
    private static Part _hover;
    private static Part _pressed;
    private static bool _trackingLeave;

    public static void ShowOffer(ReleaseInfo release, bool deferWhileBusy)
    {
        EnsureCreated();
        User32.KillTimer(_hwnd, TimerIds.UpdateToastRetryTimer);

        if (deferWhileBusy && UserIsBusy())
        {
            _deferred = release;
            User32.SetTimer(_hwnd, TimerIds.UpdateToastRetryTimer, BusyRetryMs, 0);
            return;
        }

        _deferred = null;
        _release = release;
        _mode = Mode.Offer;
        Place();
    }

    public static void ShowProgress(Version version, bool willRestart)
    {
        EnsureCreated();
        _installingVersion = version;
        _willRestart = willRestart;
        _stage = UpdateInstaller.Stage.Downloading;
        _percent = 0;
        _mode = Mode.Progress;
        Place();
    }

    public static void SetProgress(UpdateInstaller.Stage stage, int percent)
    {
        _stage = stage;
        _percent = Math.Clamp(percent, 0, 100);
        if (_hwnd != 0)
        {
            Invalidate();
        }
    }

    public static void Hide()
    {
        if (_hwnd != 0)
        {
            User32.ShowWindow(_hwnd, User32.SW_HIDE);
        }
    }

    // Full-screen game/video, presentation mode, or a "busy" fullscreen app (QUNS_BUSY).
    private static bool UserIsBusy() =>
        Shell32.SHQueryUserNotificationState(out var state) == 0 &&
        state is Shell32.QUNS_BUSY or Shell32.QUNS_RUNNING_D3D_FULL_SCREEN or Shell32.QUNS_PRESENTATION_MODE;

    private static void EnsureCreated()
    {
        if (_hwnd != 0)
        {
            return;
        }

        var hInstance = User32.GetModuleHandleW(null);

        unsafe
        {
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = User32.CS_DROPSHADOW,
                lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&WndProc,
                hInstance = hInstance,
                hCursor = User32.LoadCursorW(0, User32.IDC_ARROW),
                lpszClassName = ClassName,
            };
            User32.RegisterClassExW(in wndClass);
        }

        _hwnd = User32.CreateWindowExW(
            User32.WS_EX_TOOLWINDOW | User32.WS_EX_TOPMOST | User32.WS_EX_NOACTIVATE,
            ClassName, "Tinnitdown update", User32.WS_POPUP,
            0, 0, 1, 1, 0, 0, hInstance, 0);
    }

    // Sizes for the current mode and the primary monitor's DPI, and docks to its bottom-right corner.
    private static void Place()
    {
        Theme.Refresh();

        var (monitor, dpi) = User32.PrimaryMonitor();
        if (_fonts is null || _fonts.Dpi != dpi)
        {
            _fonts?.Dispose();
            _fonts = new UiFonts(dpi);
        }

        var f = _fonts;
        var w = f.Px(WidthDip);
        var h = f.Px(_mode == Mode.Offer ? OfferHeightDip : ProgressHeightDip);
        var margin = f.Px(MarginDip);
        var work = monitor.rcWork;

        _hover = Part.None;
        _pressed = Part.None;

        Theme.ApplyFrame(_hwnd, isPopup: true);
        User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST, work.Right - w - margin, work.Bottom - h - margin, w, h,
            User32.SWP_SHOWWINDOW | User32.SWP_NOACTIVATE);
        Invalidate();
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case User32.WM_PAINT:
                Paint();
                return 0;

            case User32.WM_ERASEBKGND:
                return 1;

            case User32.WM_MOUSEMOVE:
                OnMouseMove(MouseX(lParam), MouseY(lParam));
                return 0;

            case User32.WM_MOUSELEAVE:
                _trackingLeave = false;
                SetHover(Part.None);
                return 0;

            case User32.WM_LBUTTONDOWN:
                _pressed = HitTest(MouseX(lParam), MouseY(lParam));
                Invalidate();
                return 0;

            case User32.WM_LBUTTONUP:
                OnMouseUp(MouseX(lParam), MouseY(lParam));
                return 0;

            case User32.WM_TIMER:
                if (wParam == TimerIds.UpdateToastRetryTimer && _deferred is { } release && !UserIsBusy())
                {
                    ShowOffer(release, deferWhileBusy: false);
                }
                return 0;

            case User32.WM_DPICHANGED:
                // Place() sizes the window for the primary monitor explicitly.
                return 0;

            case User32.WM_SETTINGCHANGE:
            case User32.WM_DWMCOLORIZATIONCOLORCHANGED:
                Theme.Refresh();
                Theme.ApplyFrame(_hwnd, isPopup: true);
                Invalidate();
                return 0;
        }

        return User32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static int MouseX(nint lParam) => (short)(lParam & 0xFFFF);
    private static int MouseY(nint lParam) => (short)((lParam >> 16) & 0xFFFF);

    private static void Invalidate() => User32.InvalidateRect(_hwnd, 0, false);

    private static UiRect ButtonRect(UiFonts f, int clientW, int index) =>
        new(f.Px(PadDip), f.Px(ButtonsYDip + index * (ButtonHDip + ButtonGapDip)), clientW - 2 * f.Px(PadDip), f.Px(ButtonHDip));

    private static Part HitTest(int x, int y)
    {
        if (_mode != Mode.Offer)
        {
            return Part.None;
        }

        var f = _fonts!;
        User32.GetClientRect(_hwnd, out var rc);
        if (ButtonRect(f, rc.Right, 0).Contains(x, y))
        {
            return Part.InstallNow;
        }
        if (ButtonRect(f, rc.Right, 1).Contains(x, y))
        {
            return Part.InstallLater;
        }
        if (ButtonRect(f, rc.Right, 2).Contains(x, y))
        {
            return Part.RemindLater;
        }
        return Part.None;
    }

    private static void SetHover(Part part)
    {
        if (_hover != part)
        {
            _hover = part;
            Invalidate();
        }
    }

    private static void OnMouseMove(int x, int y)
    {
        if (!_trackingLeave)
        {
            var tme = new TRACKMOUSEEVENT
            {
                cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = User32.TME_LEAVE,
                hwndTrack = _hwnd,
            };
            User32.TrackMouseEvent(ref tme);
            _trackingLeave = true;
        }

        SetHover(HitTest(x, y));
    }

    private static void OnMouseUp(int x, int y)
    {
        var released = HitTest(x, y);
        var pressed = _pressed;
        _pressed = Part.None;
        Invalidate();

        if (released != pressed || _release is not { } release)
        {
            return;
        }

        switch (released)
        {
            case Part.InstallNow:
                UpdateInstaller.Start(release, restartWhenDone: true);
                break;
            case Part.InstallLater:
                UpdateInstaller.Start(release, restartWhenDone: false);
                break;
            case Part.RemindLater:
                // Nothing to remember: the startup check offers it again on the next launch.
                Hide();
                break;
        }
    }

    private static void Paint()
    {
        var hdc = User32.BeginPaint(_hwnd, out var ps);
        User32.GetClientRect(_hwnd, out var rc);
        PaintContent(hdc, _fonts!, rc.Right, rc.Bottom);
        User32.EndPaint(_hwnd, in ps);
    }

    private static void PaintContent(nint hdc, UiFonts f, int w, int h)
    {
        var t = Theme.P;

        using var p = new Painter(hdc, w, h);
        if (Theme.IsWindows11)
        {
            // Windows 11 draws the rounded border and shadow itself.
            p.FillRect(0, 0, w, h, t.FlyoutBg);
        }
        else
        {
            var b = f.Hairline;
            p.FillRect(0, 0, w, h, t.FlyoutBorder);
            p.FillRect(b, b, w - 2 * b, h - 2 * b, t.FlyoutBg);
        }

        var pad = f.Px(PadDip);
        var textX = f.Px(TextXDip);
        var title = new UiRect(textX, f.Px(TitleYDip), w - textX - pad, f.Px(LineHDip));
        var subtitle = title with { Y = f.Px(SubtitleYDip) };
        Fluent.Glyph(p, f.Icon, Fluent.GlyphUpdate, t.Accent, title with { X = pad, W = f.Px(IconBoxDip) });

        if (_mode == Mode.Offer)
        {
            p.Text($"New version available: {_release?.Version}", f.BodyStrong, t.TextPrimary, title, Fluent.TextLeft);
            p.Text($"Current version: {UpdateChecker.CurrentVersion}", f.Caption, t.TextSecondary, subtitle, Fluent.TextLeft);

            Fluent.AccentButton(p, f, ButtonRect(f, w, 0), "Install and restart",
                hover: _hover == Part.InstallNow, pressed: _pressed == Part.InstallNow);
            Fluent.Button(p, f, ButtonRect(f, w, 1), "Install and restart later",
                hover: _hover == Part.InstallLater, pressed: _pressed == Part.InstallLater, enabled: true);
            Fluent.Button(p, f, ButtonRect(f, w, 2), "Remind me later",
                hover: _hover == Part.RemindLater, pressed: _pressed == Part.RemindLater, enabled: true);
        }
        else
        {
            var (heading, detail) = _stage switch
            {
                UpdateInstaller.Stage.Downloading => ($"Downloading {_installingVersion}…", $"{_percent}%"),
                UpdateInstaller.Stage.Verifying => ("Verifying download…", "Checking it matches the release"),
                UpdateInstaller.Stage.Installing => ($"Installing {_installingVersion}…", "Replacing files"),
                _ => ("Restarting…", "Opening"),
            };
            var steps = _willRestart ? 4 : 3;
            var step = (int)_stage;

            p.Text(heading, f.BodyStrong, t.TextPrimary, title, Fluent.TextLeft);
            p.Text(detail, f.Caption, t.TextSecondary, subtitle, Fluent.TextLeft);
            p.Text($"Step {step + 1} of {steps}", f.Caption, t.TextSecondary, subtitle, Fluent.TextRight);
            Fluent.StepProgressBar(p, f, new UiRect(pad, f.Px(ProgressYDip), w - 2 * pad, f.Px(ProgressHDip)),
                steps, step, _percent);
        }
    }
}
