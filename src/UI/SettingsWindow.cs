using System.Runtime.InteropServices;
using System.Text;
using Shhtartup.Interop;

namespace Shhtartup;

// Windows 11 Settings-style window: cards with a custom-drawn Fluent slider, number box, toggle
// switches, a games list with check boxes, and a button. The only native control is the edit box
// inside the number box, so typing, selection and clipboard work as usual.
internal static class SettingsWindow
{
    private const string ClassName = "ShhtartupSettingsWindowClass";
    private const int EditId = 101;
    private const int IDCANCEL = 2;

    // Layout in device-independent pixels. The height grows while the games list is open.
    private const int ClientWDip = 460;
    private const int BottomMarginDip = 16;
    private const int CardXDip = 16;
    private const int CardWDip = 428;
    private const int ContentXDip = 32;
    private const int ContentRightDip = 428;

    // Cards top to bottom, CardGapDip apart. The "Adjust volume every launch" card holds the games list
    // while the setting is off, so everything below it moves down then.
    private const int VolumeCardYDip = 16;
    private const int VolumeCardHDip = 114;
    private const int EveryLaunchCardYDip = 138;
    private const int CardGapDip = 8;
    private const int ToggleCardHDip = 52;
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

    // The games list inside the "Adjust volume every launch" card.
    private const int ListHeaderHDip = 32;
    private const int RowHDip = 36;
    private const int ListBottomPadDip = 8;
    private const int MaxVisibleRows = 10; // then it scrolls, so the window never gets too tall
    private const int CheckXDip = 408;
    private const int CheckSizeDip = 20;

    // Hovering something with a tooltip this long shows it.
    private const uint TipDelayMs = 1000;
    private const string EveryLaunchTip =
        "Global setting. Checks and adjusts the volume of each game/app every time it launches. " +
        "If turned off, you can set each individual game/app below.";
    private const string StartupTip = "Runs Shhtartup automatically when Windows starts.";

    // EveryLaunchCard/StartupCard: the rest of those cards, which show the same tooltip as their toggle.
    private enum Part { None, Slider, Box, SpinUp, SpinDown, EveryLaunchToggle, EveryLaunchCard, Toggle, StartupCard, Forget, Row }

    // What's under the mouse. Row is the index into _games, for Part.Row only.
    private readonly record struct Hit(Part Part, int Row = -1);

    public static nint Hwnd { get; private set; }

    private static nint _editHwnd;
    private static UiFonts? _fonts;
    private static nint _editBrush;
    private static Rgb _editBrushColor;
    private static Hit _hover;
    private static Hit _pressed;
    private static bool _dragging;
    private static bool _trackingLeave;
    private static bool _editFocused;
    private static bool _syncing;
    private static bool _autoStart;

    private static List<KnownGame> _games = [];
    private static int _firstRow;    // scroll position, in rows
    private static int _visibleRows; // rows the open list has room for
    private static int _wheelDelta;  // unused part of a mouse-wheel scroll over the list

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
        UpdateLayout();
        Invalidate();
    }

    // A game was added or the list was cleared.
    public static void OnKnownGamesChanged()
    {
        if (Hwnd == 0)
        {
            return;
        }

        UpdateLayout();
        SetHover(default);
        Invalidate();
    }

    // The games list shows only while "Adjust volume every launch" is off (each game can then be set apart).
    private static bool ListOpen => !Settings.Current.AdjustEveryLaunch;

    // Column labels plus the visible rows, or a one-line note while there are no games yet.
    private static int ListBodyHDip => _games.Count == 0 ? RowHDip : ListHeaderHDip + _visibleRows * RowHDip;

    private static int EveryLaunchCardHDip =>
        ListOpen ? ToggleCardHDip + ListBodyHDip + ListBottomPadDip : ToggleCardHDip;

    private static int StartupCardYDip => EveryLaunchCardYDip + EveryLaunchCardHDip + CardGapDip;
    private static int MemoryCardYDip => StartupCardYDip + ToggleCardHDip + CardGapDip;
    private static int ClientHDip => MemoryCardYDip + SmallCardHDip + BottomMarginDip;

    private const uint WindowStyle = User32.WS_OVERLAPPED | User32.WS_CAPTION | User32.WS_SYSMENU | User32.WS_MINIMIZEBOX | User32.WS_CLIPCHILDREN;

    // Outer window size for a client area of ClientWDip x clientHDip.
    private static (int W, int H) WindowSize(UiFonts f, int clientHDip)
    {
        var frame = new RECT { Right = f.Px(ClientWDip), Bottom = f.Px(clientHDip) };
        User32.AdjustWindowRectExForDpi(ref frame, WindowStyle, false, 0, f.Dpi);
        return (frame.Right - frame.Left, frame.Bottom - frame.Top);
    }

    // Re-reads the games list and resizes the window to fit it, keeping it on screen: the list shows
    // up to MaxVisibleRows rows (fewer on a short screen) and scrolls beyond that.
    private static void UpdateLayout()
    {
        var f = _fonts!;
        _games = KnownGames.Sorted();

        var (monitor, _) = User32.MonitorOfWindow(Hwnd);
        var work = monitor.rcWork;
        // The window without the list, then however many rows fit in what's left of the screen.
        var withoutListDip = EveryLaunchCardYDip + 2 * ToggleCardHDip + SmallCardHDip + 2 * CardGapDip + BottomMarginDip;
        var (_, withoutListH) = WindowSize(f, withoutListDip);
        var spareDip = (work.Bottom - work.Top - withoutListH) * 96 / (int)f.Dpi - ListHeaderHDip - ListBottomPadDip;
        var fits = Math.Max(1, spareDip / RowHDip);
        _visibleRows = Math.Min(_games.Count, Math.Min(MaxVisibleRows, fits));
        _firstRow = Math.Clamp(_firstRow, 0, Math.Max(0, _games.Count - _visibleRows));

        var (w, h) = WindowSize(f, ClientHDip);
        User32.GetWindowRect(Hwnd, out var r);
        var top = r.Top + h > work.Bottom ? Math.Max(work.Top, work.Bottom - h) : r.Top;
        User32.SetWindowPos(Hwnd, 0, r.Left, top, w, h, User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
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
                hIcon = AppIcons.App,
                hCursor = User32.LoadCursorW(0, User32.IDC_ARROW),
                lpszClassName = ClassName,
            };
            User32.RegisterClassExW(in wndClass);
        }

        // Open centred on the monitor the user is working on, sized for its DPI.
        var (monitor, dpi) = User32.MonitorAtCursor();
        _fonts = new UiFonts(dpi);

        var (w, h) = WindowSize(_fonts, ClientHDip);
        var work = monitor.rcWork;
        var x = work.Left + (work.Right - work.Left - w) / 2;
        var y = work.Top + (work.Bottom - work.Top - h) / 2;

        Hwnd = User32.CreateWindowExW(0, ClassName, "Settings", WindowStyle, x, y, w, h, 0, 0, hInstance, 0);

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

    private static void Hide()
    {
        HideTip();
        User32.ShowWindow(Hwnd, User32.SW_HIDE);
    }

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
                    SetHover(default);
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
                    _pressed = default;
                    CommitVolume();
                }
                return 0;

            case User32.WM_MOUSEWHEEL:
                OnMouseWheel((short)((wParam >> 16) & 0xFFFF), lParam);
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

            case User32.WM_TIMER:
                if (wParam == TimerIds.TipTimer)
                {
                    ShowTip();
                }
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

    // The header of a toggle card (the "Adjust volume every launch" card is taller while its list shows).
    private static UiRect ToggleCardHeader(UiFonts f, int cardYDip) => CardRect(f, cardYDip, ToggleCardHDip);

    private static UiRect ToggleRect(UiFonts f, int cardYDip)
    {
        var card = ToggleCardHeader(f, cardYDip);
        var toggle = Fluent.ToggleRect(f, f.Px(ToggleXDip), 0);
        return toggle with { Y = card.Y + (card.H - toggle.H) / 2 };
    }

    // The toggle plus its "On"/"Off" label are clickable, like in Windows Settings.
    private static UiRect ToggleHitRect(UiFonts f, int cardYDip)
    {
        var toggle = ToggleRect(f, cardYDip);
        var labelW = f.Px(ToggleLabelWDip);
        return new UiRect(toggle.X - labelW, toggle.Y - f.Px(6), labelW + toggle.W, toggle.H + f.Px(12));
    }

    private static UiRect ButtonRect(UiFonts f)
    {
        var card = CardRect(f, MemoryCardYDip, SmallCardHDip);
        var h = f.Px(ControlsHDip);
        return new UiRect(f.Px(ButtonXDip), card.Y + (card.H - h) / 2, f.Px(ButtonWDip), h);
    }

    // The visible (scrolled) rows, below the toggle and column labels.
    private static UiRect ListRect(UiFonts f) =>
        f.Rect(CardXDip, EveryLaunchCardYDip + ToggleCardHDip + ListHeaderHDip, CardWDip, _visibleRows * RowHDip);

    // visibleIndex counts from the top of the visible rows.
    private static UiRect RowRect(UiFonts f, int visibleIndex)
    {
        var list = ListRect(f);
        var inset = f.Px(4);
        var rowH = f.Px(RowHDip);
        return new UiRect(list.X + inset, list.Y + visibleIndex * rowH, list.W - 2 * inset, rowH);
    }

    private static UiRect CheckRect(UiFonts f, UiRect row)
    {
        var size = f.Px(CheckSizeDip);
        return new UiRect(f.Px(CheckXDip), row.Y + (row.H - size) / 2, size, size);
    }

    private static Hit HitTest(int x, int y)
    {
        var f = _fonts!;
        if (SliderRect(f).Contains(x, y)) return new(Part.Slider);
        if (SpinRect(f, SpinUpXDip).Contains(x, y)) return new(Part.SpinUp);
        if (SpinRect(f, SpinDownXDip).Contains(x, y)) return new(Part.SpinDown);
        if (BoxRect(f).Contains(x, y)) return new(Part.Box);
        if (ToggleHitRect(f, EveryLaunchCardYDip).Contains(x, y)) return new(Part.EveryLaunchToggle);
        if (ToggleHitRect(f, StartupCardYDip).Contains(x, y)) return new(Part.Toggle);
        if (ToggleCardHeader(f, EveryLaunchCardYDip).Contains(x, y)) return new(Part.EveryLaunchCard);
        if (ToggleCardHeader(f, StartupCardYDip).Contains(x, y)) return new(Part.StartupCard);
        if (_games.Count > 0 && ButtonRect(f).Contains(x, y)) return new(Part.Forget);
        if (ListOpen && _games.Count > 0 && ListRect(f).Contains(x, y))
        {
            var row = _firstRow + (y - ListRect(f).Y) / f.Px(RowHDip);
            if (row < _games.Count) return new(Part.Row, row);
        }
        return default;
    }

    // What hovering this shows after TipDelayMs, if anything.
    private static (string Text, bool Wrap)? TipFor(Hit hit) => hit.Part switch
    {
        Part.Row when hit.Row < _games.Count => (_games[hit.Row].Path, false),
        Part.EveryLaunchToggle or Part.EveryLaunchCard => (EveryLaunchTip, true),
        Part.Toggle or Part.StartupCard => (StartupTip, true),
        _ => null,
    };

    // --- Input.

    private static void SetHover(Hit part)
    {
        if (_hover != part)
        {
            // Moving within something with the same tooltip (a card and its toggle) keeps it.
            var sameTip = TipFor(_hover) == TipFor(part);
            _hover = part;
            User32.InvalidateRect(Hwnd, 0, false);

            if (!sameTip)
            {
                HideTip();
                if (TipFor(part) is not null)
                {
                    User32.SetTimer(Hwnd, TimerIds.TipTimer, TipDelayMs, 0);
                }
            }
        }
    }

    private static void ShowTip()
    {
        User32.KillTimer(Hwnd, TimerIds.TipTimer);
        if (TipFor(_hover) is { } tip && !_dragging)
        {
            HoverTip.Show(tip.Text, _fonts!, tip.Wrap);
        }
    }

    private static void HideTip()
    {
        User32.KillTimer(Hwnd, TimerIds.TipTimer);
        HoverTip.Hide();
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
        HideTip();
        _pressed = HitTest(x, y);

        if (_pressed.Part == Part.Box)
        {
            // Clicking the box (e.g. on the "%") focuses the number and selects it for overtyping.
            User32.SetFocus(_editHwnd);
            User32.SendMessageW(_editHwnd, User32.EM_SETSEL, 0, -1);
        }
        else if (_pressed.Part is not (Part.SpinUp or Part.SpinDown))
        {
            // Clicking anywhere else takes focus off the number box, which tidies up its text.
            User32.SetFocus(Hwnd);
        }

        if (_pressed.Part == Part.Slider)
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
            _pressed = default;
            User32.ReleaseCapture();
            CommitVolume();
            SetHover(HitTest(x, y));
            return;
        }

        var released = HitTest(x, y);
        var pressed = _pressed;
        _pressed = default;
        User32.InvalidateRect(Hwnd, 0, false);

        if (released != pressed)
        {
            return;
        }

        switch (released.Part)
        {
            case Part.SpinUp:
                SetVolume(Settings.Current.DefaultVolumePercent + 1, fromEdit: false, save: true);
                break;

            case Part.SpinDown:
                SetVolume(Settings.Current.DefaultVolumePercent - 1, fromEdit: false, save: true);
                break;

            case Part.EveryLaunchToggle:
                // Turning it off opens the games list; turning it on closes it.
                KnownGames.SetAdjustEveryLaunchForAll(!Settings.Current.AdjustEveryLaunch);
                UpdateLayout();
                SetHover(HitTest(x, y));
                break;

            case Part.Row:
                // _games holds the same objects KnownGames does, so the row redraws with the change.
                // Checking the last unchecked game turns the global setting on, which closes the list.
                var game = _games[released.Row];
                KnownGames.SetAdjustEveryLaunch(game.Path, game.AdjustEveryLaunch != true);
                UpdateLayout();
                SetHover(HitTest(x, y));
                break;

            case Part.Toggle:
                _autoStart = !_autoStart;
                AutoStart.SetEnabled(_autoStart);
                Settings.Current.StartWithWindows = _autoStart;
                Settings.Save();
                break;

            case Part.Forget:
                KnownGames.Clear(); // closes the list (OnKnownGamesChanged)
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
        UpdateLayout(); // the list may fit a different number of rows now
        old?.Dispose();
        Invalidate();
    }

    // Over the open games list the wheel scrolls it; anywhere else it nudges the volume.
    private static void OnMouseWheel(int delta, nint screenPos)
    {
        var pt = new POINT { X = MouseX(screenPos), Y = MouseY(screenPos) };
        User32.ScreenToClient(Hwnd, ref pt);

        if (!ListOpen || _games.Count == 0 || !ListRect(_fonts!).Contains(pt.X, pt.Y))
        {
            SetVolume(Settings.Current.DefaultVolumePercent + (delta > 0 ? 1 : -1), fromEdit: false, save: true);
            return;
        }

        // One row per wheel notch (120); touchpads send smaller steps, which add up.
        _wheelDelta += delta;
        var rows = _wheelDelta / 120;
        _wheelDelta %= 120;
        if (rows != 0)
        {
            _firstRow = Math.Clamp(_firstRow - rows, 0, Math.Max(0, _games.Count - _visibleRows));
            HideTip();
            SetHover(HitTest(pt.X, pt.Y));
            User32.InvalidateRect(Hwnd, 0, false);
        }
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
                hover: _hover.Part == Part.Slider, pressed: _dragging);

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

            PaintEveryLaunchCard(p, f);
            ToggleCard(p, f, StartupCardYDip, ToggleCardHDip, "Run on startup", _autoStart, Part.Toggle);
            PaintMemoryCard(p, f);
        }
    }

    // "Adjust volume every launch", and while it's off, every remembered game with its own check box.
    private static void PaintEveryLaunchCard(Painter p, UiFonts f)
    {
        var t = Theme.P;
        ToggleCard(p, f, EveryLaunchCardYDip, EveryLaunchCardHDip, "Adjust volume every launch",
            Settings.Current.AdjustEveryLaunch, Part.EveryLaunchToggle);
        if (!ListOpen)
        {
            return;
        }

        var card = CardRect(f, EveryLaunchCardYDip, EveryLaunchCardHDip);
        var b = f.Hairline;
        var headerBottom = card.Y + f.Px(ToggleCardHDip);
        p.FillRect(card.X + b, headerBottom, card.W - 2 * b, b, t.Divider);

        var contentX = f.Px(ContentXDip);
        var count = _games.Count;
        if (count == 0)
        {
            p.Text("Games and apps show up here after Shhtartup first adjusts them.", f.Caption, t.TextSecondary,
                new UiRect(contentX, headerBottom, f.Px(ContentRightDip) - contentX, f.Px(RowHDip)), Fluent.TextLeft);
            return;
        }

        // Column labels.
        var labelY = headerBottom + f.Px(6);
        var labelH = f.Px(ListHeaderHDip - 6);
        p.Text("Game or app", f.Caption, t.TextSecondary, new UiRect(contentX, labelY, f.Px(200), labelH), Fluent.TextLeft);
        p.Text("Every launch", f.Caption, t.TextSecondary,
            new UiRect(f.Px(ContentRightDip) - f.Px(120), labelY, f.Px(120), labelH), Fluent.TextRight);

        var nameRight = f.Px(CheckXDip - 16);
        for (var i = 0; i < _visibleRows && _firstRow + i < count; i++)
        {
            var index = _firstRow + i;
            var game = _games[index];
            var row = RowRect(f, i);
            var hover = _hover.Part == Part.Row && _hover.Row == index;
            var pressed = _pressed.Part == Part.Row && _pressed.Row == index;
            Fluent.SubtleFill(p, f, row, hover, pressed);

            // Just the name; the full path shows on hover (TipFor).
            p.Text(game.Title, f.Body, t.TextPrimary,
                new UiRect(contentX, row.Y, nameRight - contentX, row.H), Fluent.TextLeft);

            Fluent.CheckBox(p, f, CheckRect(f, row), game.AdjustEveryLaunch == true, hover,
                surface: hover ? t.SubtleHover : t.CardBg);
        }

        // Scroll position, when the list doesn't fit.
        if (count > _visibleRows)
        {
            var list = ListRect(f);
            var thumbH = Math.Max(f.Px(16), list.H * _visibleRows / count);
            var thumbY = list.Y + (list.H - thumbH) * _firstRow / (count - _visibleRows);
            var barW = f.Px(3);
            p.FillRoundRect(card.Right - f.Px(8), thumbY, barW, thumbH, barW / 2f, t.ToggleOffBorder);
        }
    }

    private static void PaintMemoryCard(Painter p, UiFonts f)
    {
        var count = _games.Count;
        Fluent.Card(p, f, CardRect(f, MemoryCardYDip, SmallCardHDip));
        CardText(p, f, MemoryCardYDip + 15, ButtonXDip - 12, "Remembered games & apps",
            count == 0 ? "None yet." : $"{count} logged and adjusted");
        Fluent.Button(p, f, ButtonRect(f), "Forget all",
            hover: _hover.Part == Part.Forget, pressed: _pressed.Part == Part.Forget, enabled: count > 0);
    }

    // A card with a title and an On/Off toggle in its header (cardHDip includes anything below that).
    private static void ToggleCard(Painter p, UiFonts f, int cardYDip, int cardHDip, string title, bool on, Part part)
    {
        var t = Theme.P;
        Fluent.Card(p, f, CardRect(f, cardYDip, cardHDip));
        var header = ToggleCardHeader(f, cardYDip);
        var x = f.Px(ContentXDip);
        p.Text(title, f.Body, t.TextPrimary,
            new UiRect(x, header.Y, f.Px(ToggleXDip - ToggleLabelWDip) - x, header.H), Fluent.TextLeft);

        var toggle = ToggleRect(f, cardYDip);
        p.Text(on ? "On" : "Off", f.Body, t.TextPrimary,
            new UiRect(toggle.X - f.Px(ToggleLabelWDip), toggle.Y, f.Px(ToggleLabelWDip - 12), toggle.H), Fluent.TextRight);
        Fluent.Toggle(p, f, toggle, on, hover: _hover.Part == part, surface: t.CardBg);
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
        Fluent.SubtleFill(p, f, r, hover: _hover.Part == part, pressed: _pressed.Part == part);
        Fluent.Glyph(p, f.IconSmall, glyph, Theme.P.TextSecondary, r);
    }
}
