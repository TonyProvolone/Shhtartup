using System.Runtime.InteropServices;
using Shhtartup.Interop;

namespace Shhtartup;

// Windows 11-style flyout shown on right-click of the tray icon: a volume header, a Fluent slider and
// three menu items. Native popup menus (TrackPopupMenuEx) can't host a slider, so this is a custom-drawn
// popup window that dismisses itself when it loses focus, like a real menu.
internal static class QuickMenu
{
    private const string ClassName = "ShhtartupQuickMenuClass";

    // Layout in device-independent pixels.
    private const int WidthDip = 280;
    private const int HeightDip = 202;
    private const int PadDip = 16;
    private const int IconBoxDip = 20;
    private const int TextXDip = 46;
    private const int HeaderYDip = 14;
    private const int HeaderHDip = 20;
    private const int SliderYDip = 40;
    private const int SliderHDip = 32;
    private const int DividerYDip = 80;
    private const int RowsYDip = 86;
    private const int RowHDip = 36;
    private const int RowInsetDip = 4;
    private const int GapDip = 8;

    private enum Part { None, Slider, Settings, Updates, Exit }

    private static nint _hwnd;
    private static UiFonts? _fonts;
    private static Part _hover;
    private static Part _pressed;
    private static bool _dragging;
    private static bool _trackingLeave;

    public static void Show()
    {
        if (_hwnd == 0)
        {
            Create();
        }

        Theme.Refresh();

        // Size for the DPI of the monitor the tray icon is on, and open above the taskbar.
        var (monitor, dpi) = User32.MonitorAtCursor();
        if (_fonts is null || _fonts.Dpi != dpi)
        {
            _fonts?.Dispose();
            _fonts = new UiFonts(dpi);
        }

        var f = _fonts;
        var w = f.Px(WidthDip);
        var h = f.Px(HeightDip);
        var gap = f.Px(GapDip);
        var work = monitor.rcWork;
        User32.GetCursorPos(out var pt);

        var x = Math.Max(work.Left + gap, Math.Min(pt.X - w / 2, work.Right - w - gap));
        var y = Math.Min(pt.Y, work.Bottom) - h - gap;
        if (y < work.Top + gap)
        {
            y = Math.Min(pt.Y + gap, work.Bottom - h - gap);
        }

        _hover = Part.None;
        _pressed = Part.None;
        _dragging = false;

        Theme.ApplyFrame(_hwnd, isPopup: true);
        User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST, x, y, w, h, User32.SWP_SHOWWINDOW);
        User32.SetForegroundWindow(_hwnd);
        User32.InvalidateRect(_hwnd, 0, false);
    }

    private static void Create()
    {
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
            User32.WS_EX_TOOLWINDOW | User32.WS_EX_TOPMOST, ClassName, "Shhtartup", User32.WS_POPUP,
            0, 0, 1, 1, 0, 0, hInstance, 0);
    }

    private static void Hide()
    {
        User32.ShowWindow(_hwnd, User32.SW_HIDE);
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
                if (!_dragging)
                {
                    SetHover(Part.None);
                }
                return 0;

            case User32.WM_LBUTTONDOWN:
                OnMouseDown(MouseX(lParam), MouseY(lParam));
                return 0;

            case User32.WM_LBUTTONUP:
                OnMouseUp(MouseX(lParam), MouseY(lParam));
                return 0;

            case User32.WM_CAPTURECHANGED:
                if (_dragging)
                {
                    _dragging = false;
                    _pressed = Part.None;
                    Commit();
                }
                return 0;

            case User32.WM_MOUSEWHEEL:
                Step((short)((wParam >> 16) & 0xFFFF) > 0 ? 1 : -1);
                return 0;

            case User32.WM_KEYDOWN:
                OnKey((int)wParam);
                return 0;

            case User32.WM_ACTIVATE:
                // WA_INACTIVE: the user clicked elsewhere -- dismiss like a menu.
                if ((wParam & 0xFFFF) == 0)
                {
                    Hide();
                }
                return 0;

            case User32.WM_DPICHANGED:
                // Show() sizes the window for the target monitor explicitly.
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

    private static int ClientWidth()
    {
        User32.GetClientRect(_hwnd, out var rc);
        return rc.Right;
    }

    private static UiRect SliderRect(UiFonts f, int clientW) =>
        new(f.Px(PadDip), f.Px(SliderYDip), clientW - 2 * f.Px(PadDip), f.Px(SliderHDip));

    private static UiRect RowRect(UiFonts f, int clientW, int index) =>
        new(f.Px(RowInsetDip), f.Px(RowsYDip + index * RowHDip), clientW - 2 * f.Px(RowInsetDip), f.Px(RowHDip));

    private static Part HitTest(int x, int y)
    {
        var f = _fonts!;
        var w = ClientWidth();
        if (SliderRect(f, w).Contains(x, y))
        {
            return Part.Slider;
        }
        if (RowRect(f, w, 0).Contains(x, y))
        {
            return Part.Settings;
        }
        if (RowRect(f, w, 1).Contains(x, y))
        {
            return Part.Updates;
        }
        if (RowRect(f, w, 2).Contains(x, y))
        {
            return Part.Exit;
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

        if (_dragging)
        {
            SetVolumeAt(x);
            return;
        }

        SetHover(HitTest(x, y));
    }

    private static void OnMouseDown(int x, int y)
    {
        _pressed = HitTest(x, y);
        if (_pressed == Part.Slider)
        {
            _dragging = true;
            User32.SetCapture(_hwnd);
            SetVolumeAt(x);
        }
        Invalidate();
    }

    private static void OnMouseUp(int x, int y)
    {
        if (_dragging)
        {
            // Clear the flag first so WM_CAPTURECHANGED from ReleaseCapture doesn't commit twice.
            _dragging = false;
            _pressed = Part.None;
            User32.ReleaseCapture();
            Commit();
            SetHover(HitTest(x, y));
            Invalidate();
            return;
        }

        var released = HitTest(x, y);
        var pressed = _pressed;
        _pressed = Part.None;
        Invalidate();

        if (released != pressed)
        {
            return;
        }

        if (released == Part.Settings)
        {
            Hide();
            SettingsWindow.Show();
        }
        else if (released == Part.Updates)
        {
            Hide();
            UpdateChecker.CheckNow();
        }
        else if (released == Part.Exit)
        {
            Hide();
            TrayIcon.ExitApp();
        }
    }

    private static void OnKey(int key)
    {
        switch (key)
        {
            case User32.VK_ESCAPE: Hide(); break;
            case User32.VK_LEFT or User32.VK_DOWN: Step(-1); break;
            case User32.VK_RIGHT or User32.VK_UP: Step(1); break;
            case User32.VK_NEXT: Step(-10); break;
            case User32.VK_PRIOR: Step(10); break;
            case User32.VK_HOME: Step(-100); break;
            case User32.VK_END: Step(100); break;
        }
    }

    // While dragging: update live, persist on release.
    private static void SetVolumeAt(int mouseX)
    {
        var value = Fluent.SliderValueAt(_fonts!, SliderRect(_fonts!, ClientWidth()), mouseX);
        if (value != Settings.Current.DefaultVolumePercent)
        {
            Settings.SetDefaultVolume(value, save: false);
            Invalidate();
        }
    }

    private static void Step(int delta)
    {
        Settings.SetDefaultVolume(Settings.Current.DefaultVolumePercent + delta, save: false);
        Commit();
        Invalidate();
    }

    private static void Commit()
    {
        Settings.Save();
        TrayIcon.UpdateTooltip();
        SettingsWindow.SyncFromSettings();
    }

    private static void Paint()
    {
        var hdc = User32.BeginPaint(_hwnd, out var ps);
        User32.GetClientRect(_hwnd, out var rc);
        PaintContent(hdc, _fonts!, rc.Right, rc.Bottom);
        User32.EndPaint(_hwnd, in ps);
    }

    // Renders the flyout into any device context without creating a window (design previews).
    internal static (int W, int H) RenderPreview(nint hdc, UiFonts fonts, bool hoverSettings)
    {
        _fonts = fonts;
        _hover = hoverSettings ? Part.Settings : Part.None;
        var w = fonts.Px(WidthDip);
        var h = fonts.Px(HeightDip);
        PaintContent(hdc, fonts, w, h);
        return (w, h);
    }

    private static void PaintContent(nint hdc, UiFonts f, int w, int h)
    {
        var t = Theme.P;

        using (var p = new Painter(hdc, w, h))
        {
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
            var header = new UiRect(pad, f.Px(HeaderYDip), w - 2 * pad, f.Px(HeaderHDip));
            Fluent.Glyph(p, f.Icon, Fluent.GlyphVolume, t.TextPrimary, header with { W = f.Px(IconBoxDip) });
            p.Text("Default volume", f.Body, t.TextPrimary, header with { X = f.Px(TextXDip), W = header.Right - f.Px(TextXDip) }, Fluent.TextLeft);
            p.Text($"{Settings.Current.DefaultVolumePercent}%", f.BodyStrong, t.TextPrimary, header, Fluent.TextRight);

            Fluent.Slider(p, f, SliderRect(f, w), Settings.Current.DefaultVolumePercent,
                hover: _hover == Part.Slider, pressed: _dragging);

            p.FillRect(0, f.Px(DividerYDip), w, f.Hairline, t.Divider);

            DrawRow(p, f, w, 0, Fluent.GlyphSettings, "Settings", Part.Settings);
            DrawRow(p, f, w, 1, Fluent.GlyphUpdate, "Check for updates", Part.Updates);
            DrawRow(p, f, w, 2, Fluent.GlyphPower, "Exit", Part.Exit);
        }
    }

    private static void DrawRow(Painter p, UiFonts f, int clientW, int index, char glyph, string text, Part part)
    {
        var t = Theme.P;
        var row = RowRect(f, clientW, index);
        Fluent.SubtleFill(p, f, row, hover: _hover == part, pressed: _pressed == part);
        Fluent.Glyph(p, f.Icon, glyph, t.TextPrimary, row with { X = f.Px(PadDip), W = f.Px(IconBoxDip) });
        p.Text(text, f.Body, t.TextPrimary, row with { X = f.Px(TextXDip), W = row.Right - f.Px(TextXDip) }, Fluent.TextLeft);
    }
}
