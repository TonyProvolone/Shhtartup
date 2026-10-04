using System.Runtime.InteropServices;
using System.Text;
using Tinnitdown.Interop;

namespace Tinnitdown;

// Windows 11 Settings-style window: three cards with a custom-drawn Fluent slider, number box,
// toggle switch and button. The only native control is the edit box inside the number box, so
// typing, selection and clipboard work as usual.
internal static class SettingsWindow
{
    private const string ClassName = "TinnitdownSettingsWindowClass";
    private const int EditId = 101;
    private const int IDCANCEL = 2;

    // Layout in device-independent pixels.
    private const int ClientWDip = 460;
    private const int ClientHDip = 298;
    private const int CardXDip = 16;
    private const int CardWDip = 428;
    private const int ContentXDip = 32;
    private const int ContentRightDip = 428;

    private const int VolumeCardYDip = 16;
    private const int VolumeCardHDip = 114;
    private const int StartupCardYDip = 138;
    private const int MemoryCardYDip = 214;
    private const int SmallCardHDip = 68;

    private const int SliderXDip = 32;
    private const int SliderWDip = 268;
    private const int ControlsYDip = 78;
    private const int ControlsHDip = 32;
    private const int BoxXDip = 316;
    private const int BoxWDip = 112;

    // Inside the number box, relative to its left edge.
    private const int EditXDip = 8;
    private const int EditWDip = 40;
    private const int EditHDip = 20;
    private const int PercentXDip = 48;
    private const int PercentWDip = 12;
    private const int SpinUpXDip = 66;
    private const int SpinDownXDip = 88;
    private const int SpinWDip = 20;
    private const int SpinHDip = 24;

    private const int ToggleXDip = 388;
    private const int ToggleLabelWDip = 60;
    private const int ButtonXDip = 332;
    private const int ButtonWDip = 96;

    private enum Part { None, Slider, Box, SpinUp, SpinDown, Toggle, Forget }

    public static nint Hwnd { get; private set; }

    private static nint _editHwnd;
    private static UiFonts? _fonts;
    private static nint _editBrush;
    private static Rgb _editBrushColor;
    private static Part _hover;
    private static Part _pressed;
    private static bool _dragging;
    private static bool _trackingLeave;
    private static bool _editFocused;
    private static bool _syncing;
    private static bool _autoStart;

    public static void Show()
    {
        Theme.Refresh();
        if (Hwnd == 0)
        {
            Create();
        }

        Theme.ApplyFrame(Hwnd, isPopup: false);
        SyncFromSettings();
        User32.ShowWindow(Hwnd, User32.SW_SHOW);
        User32.SetForegroundWindow(Hwnd);
    }

    // Pulls current values into the window (e.g. after the tray flyout changed the volume).
    public static void SyncFromSettings()
    {
        if (Hwnd == 0)
        {
            return;
        }

        _autoStart = AutoStart.IsEnabled();
        SetEditText(Settings.Current.DefaultVolumePercent);
        Invalidate();
    }

    // Called from the message loop before dispatch: arrow/page keys step the value, matching the
    // keyboard behaviour of a standard number box. Left/Right are left alone inside the edit (caret).
    public static bool TryHandleKey(in MSG msg)
    {
        if (Hwnd == 0 || msg.message != User32.WM_KEYDOWN || (msg.hwnd != _editHwnd && msg.hwnd != Hwnd))
        {
            return false;
        }

        var delta = (int)msg.wParam switch
        {
            User32.VK_UP => 1,
            User32.VK_DOWN => -1,
            User32.VK_PRIOR => 10,
            User32.VK_NEXT => -10,
            User32.VK_RIGHT when msg.hwnd == Hwnd => 1,
            User32.VK_LEFT when msg.hwnd == Hwnd => -1,
            _ => 0,
        };
        if (delta == 0)
        {
            return false;
        }

        SetVolume(Settings.Current.DefaultVolumePercent + delta, fromEdit: false, save: true);
        return true;
    }

    private static void Create()
    {
        var hInstance = User32.GetModuleHandleW(null);

        unsafe
        {
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&WndProc,
                hInstance = hInstance,
                hIcon = User32.LoadIconW(0, User32.IDI_APPLICATION),
                hCursor = User32.LoadCursorW(0, User32.IDC_ARROW),
                lpszClassName = ClassName,
            };
            User32.RegisterClassExW(in wndClass);
        }

        // Open centred on the monitor the user is working on, sized for its DPI.
        var (monitor, dpi) = User32.MonitorAtCursor();
        _fonts = new UiFonts(dpi);

        const uint style = User32.WS_OVERLAPPED | User32.WS_CAPTION | User32.WS_SYSMENU | User32.WS_MINIMIZEBOX | User32.WS_CLIPCHILDREN;
        var frame = new RECT { Right = _fonts.Px(ClientWDip), Bottom = _fonts.Px(ClientHDip) };
        User32.AdjustWindowRectExForDpi(ref frame, style, false, 0, dpi);
        var w = frame.Right - frame.Left;
        var h = frame.Bottom - frame.Top;
        var work = monitor.rcWork;
        var x = work.Left + (work.Right - work.Left - w) / 2;
        var y = work.Top + (work.Bottom - work.Top - h) / 2;

        Hwnd = User32.CreateWindowExW(0, ClassName, "Settings", style, x, y, w, h, 0, 0, hInstance, 0);

        _editHwnd = User32.CreateWindowExW(0, "Edit", "",
            User32.WS_CHILD | User32.WS_VISIBLE | User32.WS_TABSTOP | User32.ES_NUMBER | User32.ES_RIGHT,
            0, 0, 1, 1, Hwnd, EditId, hInstance, 0);
        User32.SendMessageW(_editHwnd, User32.EM_LIMITTEXT, 3, 0);
        User32.SendMessageW(_editHwnd, User32.EM_SETMARGINS, User32.EC_LEFTMARGIN | User32.EC_RIGHTMARGIN, 0);
        LayoutEdit();
    }

    private static void LayoutEdit()
    {
        var f = _fonts!;
        var box = BoxRect(f);
        var editH = f.Px(EditHDip);
        User32.SendMessageW(_editHwnd, User32.WM_SETFONT, unchecked((nuint)f.Body), 1);
        User32.SetWindowPos(_editHwnd, 0, box.X + f.Px(EditXDip), box.Y + (box.H - editH) / 2, f.Px(EditWDip), editH,
            User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
    }

    private static void Hide() => User32.ShowWindow(Hwnd, User32.SW_HIDE);

    private static void Invalidate()
    {
        User32.InvalidateRect(Hwnd, 0, false);
        User32.InvalidateRect(_editHwnd, 0, true);
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

            case User32.WM_CTLCOLOREDIT:
                return EditColors((nint)wParam);

            case User32.WM_COMMAND:
                OnCommand(wParam);
                return 0;

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
                    CommitVolume();
                }
                return 0;

            case User32.WM_MOUSEWHEEL:
                SetVolume(Settings.Current.DefaultVolumePercent + ((short)((wParam >> 16) & 0xFFFF) > 0 ? 1 : -1),
                    fromEdit: false, save: true);
                return 0;

            case User32.WM_DPICHANGED:
                OnDpiChanged((uint)(wParam & 0xFFFF), lParam);
                return 0;

            case User32.WM_SETTINGCHANGE:
            case User32.WM_DWMCOLORIZATIONCOLORCHANGED:
                Theme.Refresh();
                Theme.ApplyFrame(Hwnd, isPopup: false);
                Invalidate();
                return 0;

            case User32.WM_CLOSE:
                Hide();
                return 0;
        }

        return User32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static int MouseX(nint lParam) => (short)(lParam & 0xFFFF);
    private static int MouseY(nint lParam) => (short)((lParam >> 16) & 0xFFFF);

    // --- Layout.

    private static UiRect CardRect(UiFonts f, int yDip, int hDip) => f.Rect(CardXDip, yDip, CardWDip, hDip);
    private static UiRect SliderRect(UiFonts f) => f.Rect(SliderXDip, ControlsYDip, SliderWDip, ControlsHDip);
    private static UiRect BoxRect(UiFonts f) => f.Rect(BoxXDip, ControlsYDip, BoxWDip, ControlsHDip);

    private static UiRect SpinRect(UiFonts f, int offsetDip)
    {
        var box = BoxRect(f);
        var h = f.Px(SpinHDip);
        return new UiRect(box.X + f.Px(offsetDip), box.Y + (box.H - h) / 2, f.Px(SpinWDip), h);
    }

    private static UiRect ToggleRect(UiFonts f)
    {
        var card = CardRect(f, StartupCardYDip, SmallCardHDip);
        var toggle = Fluent.ToggleRect(f, f.Px(ToggleXDip), 0);
        return toggle with { Y = card.Y + (card.H - toggle.H) / 2 };
    }

    // The toggle plus its "On"/"Off" label are clickable, like in Windows Settings.
    private static UiRect ToggleHitRect(UiFonts f)
    {
        var toggle = ToggleRect(f);
        var labelW = f.Px(ToggleLabelWDip);
        return new UiRect(toggle.X - labelW, toggle.Y - f.Px(6), labelW + toggle.W, toggle.H + f.Px(12));
    }

    private static UiRect ButtonRect(UiFonts f)
    {
        var card = CardRect(f, MemoryCardYDip, SmallCardHDip);
        var h = f.Px(ControlsHDip);
        return new UiRect(f.Px(ButtonXDip), card.Y + (card.H - h) / 2, f.Px(ButtonWDip), h);
    }

    private static Part HitTest(int x, int y)
    {
        var f = _fonts!;
        if (SliderRect(f).Contains(x, y)) return Part.Slider;
        if (SpinRect(f, SpinUpXDip).Contains(x, y)) return Part.SpinUp;
        if (SpinRect(f, SpinDownXDip).Contains(x, y)) return Part.SpinDown;
        if (BoxRect(f).Contains(x, y)) return Part.Box;
        if (ToggleHitRect(f).Contains(x, y)) return Part.Toggle;
        if (ButtonRect(f).Contains(x, y) && KnownGames.Count > 0) return Part.Forget;
        return Part.None;
    }

    // --- Input.

    private static void SetHover(Part part)
    {
        if (_hover != part)
        {
            _hover = part;
            User32.InvalidateRect(Hwnd, 0, false);
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
                hwndTrack = Hwnd,
            };
            User32.TrackMouseEvent(ref tme);
            _trackingLeave = true;
        }

        if (_dragging)
        {
            SetVolume(Fluent.SliderValueAt(_fonts!, SliderRect(_fonts!), x), fromEdit: false, save: false);
            return;
        }

        SetHover(HitTest(x, y));
    }

    private static void OnMouseDown(int x, int y)
    {
        _pressed = HitTest(x, y);

        if (_pressed == Part.Box)
        {
            // Clicking the box (e.g. on the "%") focuses the number and selects it for overtyping.
            User32.SetFocus(_editHwnd);
            User32.SendMessageW(_editHwnd, User32.EM_SETSEL, 0, -1);
        }
        else if (_pressed is not (Part.SpinUp or Part.SpinDown))
        {
            // Clicking anywhere else takes focus off the number box, which tidies up its text.
            User32.SetFocus(Hwnd);
        }

        if (_pressed == Part.Slider)
        {
            _dragging = true;
            User32.SetCapture(Hwnd);
            SetVolume(Fluent.SliderValueAt(_fonts!, SliderRect(_fonts!), x), fromEdit: false, save: false);
        }

        User32.InvalidateRect(Hwnd, 0, false);
    }

    private static void OnMouseUp(int x, int y)
    {
        if (_dragging)
        {
            // Clear the flag first so WM_CAPTURECHANGED from ReleaseCapture doesn't commit twice.
            _dragging = false;
            _pressed = Part.None;
            User32.ReleaseCapture();
            CommitVolume();
            SetHover(HitTest(x, y));
            return;
        }

        var released = HitTest(x, y);
        var pressed = _pressed;
        _pressed = Part.None;
        User32.InvalidateRect(Hwnd, 0, false);

        if (released != pressed)
        {
            return;
        }

        switch (released)
        {
            case Part.SpinUp:
                SetVolume(Settings.Current.DefaultVolumePercent + 1, fromEdit: false, save: true);
                break;

            case Part.SpinDown:
                SetVolume(Settings.Current.DefaultVolumePercent - 1, fromEdit: false, save: true);
                break;

            case Part.Toggle:
                _autoStart = !_autoStart;
                AutoStart.SetEnabled(_autoStart);
                Settings.Current.StartWithWindows = _autoStart;
                Settings.Save();
                break;

            case Part.Forget:
                KnownGames.Clear();
                SetHover(HitTest(x, y));
                break;
        }
    }

    private static void OnCommand(nuint wParam)
    {
        var id = (int)(wParam & 0xFFFF);
        var code = (int)((wParam >> 16) & 0xFFFF);

        if (id == IDCANCEL)
        {
            // Esc (routed through IsDialogMessage).
            Hide();
            return;
        }

        if (id != EditId)
        {
            return;
        }

        switch (code)
        {
            case User32.EN_CHANGE when !_syncing:
                if (int.TryParse(GetEditText(), out var value))
                {
                    SetVolume(value, fromEdit: true, save: true);
                }
                break;

            case User32.EN_SETFOCUS:
                _editFocused = true;
                Invalidate();
                break;

            case User32.EN_KILLFOCUS:
                _editFocused = false;
                // Replace an empty or out-of-range entry with the value actually in effect.
                SetEditText(Settings.Current.DefaultVolumePercent);
                Invalidate();
                break;
        }
    }

    private static void OnDpiChanged(uint dpi, nint suggestedRect)
    {
        var old = _fonts;
        _fonts = new UiFonts(dpi);

        unsafe
        {
            var r = *(RECT*)suggestedRect;
            User32.SetWindowPos(Hwnd, 0, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }

        LayoutEdit();
        old?.Dispose();
        Invalidate();
    }

    // --- Value handling.

    private static void SetVolume(int value, bool fromEdit, bool save)
    {
        var clamped = Settings.SetDefaultVolume(value, save);

        // Don't rewrite the box while the user is typing a valid value (it would move the caret).
        if (!fromEdit || clamped != value)
        {
            SetEditText(clamped);
        }

        if (save)
        {
            TrayIcon.UpdateTooltip();
        }

        User32.InvalidateRect(Hwnd, 0, false);
    }

    private static void CommitVolume()
    {
        Settings.Save();
        TrayIcon.UpdateTooltip();
    }

    private static void SetEditText(int value)
    {
        _syncing = true;
        User32.SetWindowTextW(_editHwnd, value.ToString());
        _syncing = false;
    }

    private static string GetEditText()
    {
        var sb = new StringBuilder(8);
        User32.GetWindowTextW(_editHwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // The edit control paints its own background; match it to the number box.
    private static nint EditColors(nint hdc)
    {
        var fill = Fluent.InputFill(_editFocused);
        Gdi32.SetTextColor(hdc, Theme.P.TextPrimary.ColorRef);
        Gdi32.SetBkColor(hdc, fill.ColorRef);

        if (_editBrush == 0 || _editBrushColor != fill)
        {
            if (_editBrush != 0)
            {
                Gdi32.DeleteObject(_editBrush);
            }
            _editBrush = Gdi32.CreateSolidBrush(fill.ColorRef);
            _editBrushColor = fill;
        }
        return _editBrush;
    }

    // --- Painting.

    private static void Paint()
    {
        var hdc = User32.BeginPaint(Hwnd, out var ps);
        User32.GetClientRect(Hwnd, out var rc);
        PaintContent(hdc, _fonts!, rc.Right, rc.Bottom);
        User32.EndPaint(Hwnd, in ps);
    }

    // Renders the window's client area into any device context without creating a window (design
    // previews). The number normally drawn by the edit control is painted in its place.
    internal static (int W, int H) RenderPreview(nint hdc, UiFonts fonts, bool autoStart, bool focusBox)
    {
        _fonts = fonts;
        _autoStart = autoStart;
        _editFocused = focusBox;
        var w = fonts.Px(ClientWDip);
        var h = fonts.Px(ClientHDip);
        PaintContent(hdc, fonts, w, h);
        return (w, h);
    }

    private static void PaintContent(nint hdc, UiFonts f, int w, int h)
    {
        var t = Theme.P;

        using (var p = new Painter(hdc, w, h))
        {
            p.FillRect(0, 0, w, h, t.WindowBg);

            // Default volume.
            Fluent.Card(p, f, CardRect(f, VolumeCardYDip, VolumeCardHDip));
            CardText(p, f, VolumeCardYDip + 12, ContentRightDip,
                "Default volume", "Applied the moment a game or app first plays sound");
            Fluent.Slider(p, f, SliderRect(f), Settings.Current.DefaultVolumePercent,
                hover: _hover == Part.Slider, pressed: _dragging);

            var box = BoxRect(f);
            Fluent.InputFrame(p, f, box, _editFocused);
            p.Text("%", f.Body, t.TextSecondary, new UiRect(box.X + f.Px(PercentXDip), box.Y, f.Px(PercentWDip), box.H), Fluent.TextLeft);
            if (_editHwnd == 0)
            {
                p.Text(Settings.Current.DefaultVolumePercent.ToString(), f.Body, t.TextPrimary,
                    new UiRect(box.X + f.Px(EditXDip), box.Y, f.Px(EditWDip), box.H), Fluent.TextRight);
            }
            DrawSpin(p, f, SpinUpXDip, Fluent.GlyphChevronUp, Part.SpinUp);
            DrawSpin(p, f, SpinDownXDip, Fluent.GlyphChevronDown, Part.SpinDown);

            // Run on startup.
            Fluent.Card(p, f, CardRect(f, StartupCardYDip, SmallCardHDip));
            CardText(p, f, StartupCardYDip + 15, ToggleXDip - ToggleLabelWDip,
                "Run on startup", "Start Tinnitdown automatically on startup");
            var toggle = ToggleRect(f);
            p.Text(_autoStart ? "On" : "Off", f.Body, t.TextPrimary,
                new UiRect(toggle.X - f.Px(ToggleLabelWDip), toggle.Y, f.Px(ToggleLabelWDip - 12), toggle.H), Fluent.TextRight);
            Fluent.Toggle(p, f, toggle, _autoStart, hover: _hover == Part.Toggle, surface: t.CardBg);

            // Remembered games & apps.
            var count = KnownGames.Count;
            Fluent.Card(p, f, CardRect(f, MemoryCardYDip, SmallCardHDip));
            CardText(p, f, MemoryCardYDip + 15, ButtonXDip - 12, "Remembered games & apps",
                count == 0 ? "None yet."
                : $"{count} logged and adjusted");
            Fluent.Button(p, f, ButtonRect(f), "Forget all",
                hover: _hover == Part.Forget, pressed: _pressed == Part.Forget, enabled: count > 0);
        }
    }

    private static void CardText(Painter p, UiFonts f, int topDip, int rightDip, string title, string description)
    {
        var t = Theme.P;
        var x = f.Px(ContentXDip);
        var w = f.Px(rightDip) - x;
        p.Text(title, f.Body, t.TextPrimary, new UiRect(x, f.Px(topDip), w, f.Px(20)), Fluent.TextLeft);
        p.Text(description, f.Caption, t.TextSecondary, new UiRect(x, f.Px(topDip + 20), w, f.Px(18)), Fluent.TextLeft);
    }

    private static void DrawSpin(Painter p, UiFonts f, int offsetDip, char glyph, Part part)
    {
        var r = SpinRect(f, offsetDip);
        Fluent.SubtleFill(p, f, r, hover: _hover == part, pressed: _pressed == part);
        Fluent.Glyph(p, f.IconSmall, glyph, Theme.P.TextSecondary, r);
    }
}
