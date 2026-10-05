using System.Runtime.InteropServices;
using Shhtartup.Interop;

namespace Shhtartup;

// Small, silent pill in the bottom-right corner when a game/app's volume has just been turned down
// (skipped over exclusive-fullscreen games and presentations).
// It's purely informational and must never get in a game's way: it can't be clicked (mouse input
// passes straight through to the window underneath), never takes focus, isn't in the taskbar or
// Alt+Tab, makes no sound, and fades away by itself after a few seconds.
internal static class VolumeToast
{
    private const string ClassName = "ShhtartupVolumeToastClass";

    // Layout in device-independent pixels.
    private const int HeightDip = 40;
    private const int PadDip = 14;
    private const int IconDip = 16;
    private const int GapDip = 10;
    private const int RadiusDip = 8;
    private const int MarginDip = 16;
    private const int MaxWidthDip = 320;

    private const int FadeInMs = 150;
    private const int HoldMs = 2500;
    private const int FadeOutMs = 400;
    private const uint FrameMs = 16;
    // Slightly see-through, so it reads as an overlay rather than a window.
    private const byte MaxAlpha = 240;

    private static nint _hwnd;
    private static UiFonts? _fonts;
    private static string _text = string.Empty;
    private static long _shownAt;
    private static nint _icon;
    private static (bool Dark, int Size) _iconStyle;

    public static void Show(int percent)
    {
        // Shown over borderless (windowed) fullscreen games, like the Windows volume indicator and
        // Game Bar pop-ups, but never over exclusive fullscreen: a window appearing on top there can
        // make the game flicker or drop out of fullscreen. Presentations are left alone too.
        if (MustStayHidden())
        {
            return;
        }

        EnsureCreated();
        _text = $"Volume set to {percent}%";

        // Shown again while still up (e.g. two games launched together): carry on fading in, or go
        // back to fully shown and start the hold over.
        var now = Environment.TickCount64;
        var elapsed = now - _shownAt;
        _shownAt = elapsed < FadeInMs ? _shownAt
            : elapsed < FadeInMs + HoldMs + FadeOutMs ? now - FadeInMs
            : now;

        // Opacity first, so a fresh toast is never on screen at full strength before it fades in.
        Animate();
        Place();
        User32.SetTimer(_hwnd, TimerIds.VolumeToastTimer, FrameMs, 0);
    }

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
                lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&WndProc,
                hInstance = hInstance,
                lpszClassName = ClassName,
            };
            User32.RegisterClassExW(in wndClass);
        }

        _hwnd = User32.CreateWindowExW(
            User32.WS_EX_TOOLWINDOW | User32.WS_EX_TOPMOST | User32.WS_EX_NOACTIVATE |
            User32.WS_EX_LAYERED | User32.WS_EX_TRANSPARENT,
            ClassName, "Shhtartup", User32.WS_POPUP,
            0, 0, 1, 1, 0, 0, hInstance, 0);
        User32.SetLayeredWindowAttributes(_hwnd, 0, 0, User32.LWA_ALPHA);
    }

    // Sizes to the text and docks to the bottom-right of the monitor the game is on.
    private static void Place()
    {
        Theme.Refresh();

        var (monitor, dpi) = User32.MonitorOfForegroundWindow();
        if (_fonts is null || _fonts.Dpi != dpi)
        {
            _fonts?.Dispose();
            _fonts = new UiFonts(dpi);
        }

        var f = _fonts;
        var iconStyle = (Dark: Theme.IsDark, Size: f.Px(IconDip));
        if (_icon == 0 || iconStyle != _iconStyle)
        {
            if (_icon != 0)
            {
                User32.DestroyIcon(_icon);
            }
            // The tray glyphs: white for a dark surface, black for a light one.
            _icon = AppIcons.CreateTrayIcon(lightTaskbar: !iconStyle.Dark, iconStyle.Size);
            _iconStyle = iconStyle;
        }

        var w = Math.Min(f.Px(PadDip + IconDip + GapDip + PadDip) + TextMetrics.Width(_text, f.Body), f.Px(MaxWidthDip));
        var h = f.Px(HeightDip);
        var margin = f.Px(MarginDip);
        var work = monitor.rcWork;

        var radius = f.Px(RadiusDip);
        User32.SetWindowRgn(_hwnd, Gdi32.CreateRoundRectRgn(0, 0, w + 1, h + 1, radius * 2, radius * 2), true);
        User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST, work.Right - w - margin, work.Bottom - h - margin, w, h,
            User32.SWP_SHOWWINDOW | User32.SWP_NOACTIVATE);
        User32.InvalidateRect(_hwnd, 0, false);
    }

    private static bool MustStayHidden() =>
        Shell32.SHQueryUserNotificationState(out var state) == 0 &&
        state is Shell32.QUNS_RUNNING_D3D_FULL_SCREEN or Shell32.QUNS_PRESENTATION_MODE;

    // Fade in, hold, fade out, hide. Also hides at once if the game switches to exclusive fullscreen meanwhile.
    private static void Animate()
    {
        var t = Environment.TickCount64 - _shownAt;
        float opacity;
        if (MustStayHidden())
        {
            t = long.MaxValue;
        }

        if (t < FadeInMs)
        {
            opacity = (float)t / FadeInMs;
        }
        else if (t < FadeInMs + HoldMs)
        {
            opacity = 1;
        }
        else if (t < FadeInMs + HoldMs + FadeOutMs)
        {
            opacity = 1 - (float)(t - FadeInMs - HoldMs) / FadeOutMs;
        }
        else
        {
            User32.KillTimer(_hwnd, TimerIds.VolumeToastTimer);
            User32.ShowWindow(_hwnd, User32.SW_HIDE);
            _shownAt = 0; // the next one starts fresh
            return;
        }

        User32.SetLayeredWindowAttributes(_hwnd, 0, (byte)(MaxAlpha * opacity), User32.LWA_ALPHA);
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

            // Belt and braces with WS_EX_TRANSPARENT: never claim the mouse.
            case User32.WM_NCHITTEST:
                return User32.HTTRANSPARENT;

            case User32.WM_TIMER:
                if (wParam == TimerIds.VolumeToastTimer)
                {
                    Animate();
                }
                return 0;

            case User32.WM_DPICHANGED:
                // Place() sizes the window for its monitor explicitly.
                return 0;
        }

        return User32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void Paint()
    {
        var hdc = User32.BeginPaint(_hwnd, out var ps);
        User32.GetClientRect(_hwnd, out var rc);

        var f = _fonts!;
        var t = Theme.P;
        var w = rc.Right;
        var h = rc.Bottom;
        var b = f.Hairline;
        var radius = f.Px(RadiusDip);

        using (var p = new Painter(hdc, w, h))
        {
            // The window region does the outer rounding; the border colour shows round its edge.
            p.FillRect(0, 0, w, h, t.FlyoutBorder);
            p.FillRoundRect(b, b, w - 2 * b, h - 2 * b, radius - b, t.FlyoutBg);

            var pad = f.Px(PadDip);
            var icon = f.Px(IconDip);
            p.Icon(_icon, pad, (h - icon) / 2, icon);

            var textX = pad + icon + f.Px(GapDip);
            p.Text(_text, f.Body, t.TextPrimary, new UiRect(textX, 0, w - textX - pad, h), Fluent.TextLeft);
        }

        User32.EndPaint(_hwnd, in ps);
    }
}
