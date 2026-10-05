using System.Runtime.InteropServices;
using Shhtartup.Interop;

namespace Shhtartup;

// A small themed tooltip (e.g. a game's full path in Settings). The owner decides when to show and
// hide it. It never takes focus and lets the mouse pass through, so it can't steal the hover from
// the window it's describing.
internal static class HoverTip
{
    private const string ClassName = "ShhtartupHoverTipClass";

    private const int PadXDip = 8;
    private const int PadYDip = 6;
    private const int HeightDip = 28;
    private const int MaxWidthDip = 560;     // one line (a path is shortened in the middle to fit)
    private const int WrapWidthDip = 320;    // wrapped text
    private const int CursorOffsetDip = 20;

    private static nint _hwnd;
    private static UiFonts? _fonts;
    private static string _text = string.Empty;
    private static bool _wrap;

    // Shows the tip just below and right of the cursor, kept on the cursor's monitor. wrap: text
    // that's a sentence or two (word-wrapped); otherwise one line, like a file path.
    public static void Show(string text, UiFonts fonts, bool wrap = false)
    {
        EnsureCreated();
        _text = text;
        _fonts = fonts;
        _wrap = wrap;

        var f = fonts;
        var padX = f.Px(PadXDip);
        int w, h;
        if (wrap)
        {
            var (textW, textH) = TextMetrics.Wrapped(text, f.Caption, f.Px(WrapWidthDip));
            w = textW + 2 * padX;
            h = textH + 2 * f.Px(PadYDip);
        }
        else
        {
            w = Math.Min(TextMetrics.Width(text, f.Caption) + 2 * padX, f.Px(MaxWidthDip));
            h = f.Px(HeightDip);
        }

        User32.GetCursorPos(out var cursor);
        var (monitor, _) = User32.MonitorAtCursor();
        var work = monitor.rcWork;
        var offset = f.Px(CursorOffsetDip);
        var x = Math.Clamp(cursor.X + offset / 2, work.Left, Math.Max(work.Left, work.Right - w));
        // Below the cursor, or above it near the bottom of the screen.
        var y = cursor.Y + offset + h <= work.Bottom ? cursor.Y + offset : cursor.Y - offset / 2 - h;

        Theme.Refresh();
        User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST, x, y, w, h, User32.SWP_SHOWWINDOW | User32.SWP_NOACTIVATE);
        User32.InvalidateRect(_hwnd, 0, false);
    }

    public static void Hide()
    {
        if (_hwnd != 0)
        {
            User32.ShowWindow(_hwnd, User32.SW_HIDE);
        }
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
            ClassName, "", User32.WS_POPUP,
            0, 0, 1, 1, 0, 0, hInstance, 0);
        User32.SetLayeredWindowAttributes(_hwnd, 0, 255, User32.LWA_ALPHA);
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

            case User32.WM_NCHITTEST:
                return User32.HTTRANSPARENT;
        }

        return User32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void Paint()
    {
        var hdc = User32.BeginPaint(_hwnd, out var ps);
        User32.GetClientRect(_hwnd, out var rc);

        var f = _fonts!;
        var t = Theme.P;
        var b = f.Hairline;
        using (var p = new Painter(hdc, rc.Right, rc.Bottom))
        {
            p.FillRect(0, 0, rc.Right, rc.Bottom, t.FlyoutBorder);
            p.FillRect(b, b, rc.Right - 2 * b, rc.Bottom - 2 * b, t.FlyoutBg);
            var padX = f.Px(PadXDip);
            if (_wrap)
            {
                var padY = f.Px(PadYDip);
                p.Text(_text, f.Caption, t.TextPrimary, new UiRect(padX, padY, rc.Right - 2 * padX, rc.Bottom - 2 * padY),
                    User32.DT_LEFT | User32.DT_WORDBREAK);
            }
            else
            {
                p.Text(_text, f.Caption, t.TextPrimary, new UiRect(padX, 0, rc.Right - 2 * padX, rc.Bottom),
                    User32.DT_LEFT | User32.DT_VCENTER | User32.DT_SINGLELINE | User32.DT_PATH_ELLIPSIS);
            }
        }

        User32.EndPaint(_hwnd, in ps);
    }
}
