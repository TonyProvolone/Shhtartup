using System.Runtime.InteropServices;
using Shhtartup.Interop;

namespace Shhtartup;

// Small Fluent-styled dialog for installing and uninstalling, run modally (with its own message loop)
// before the tray app starts. Three layouts: choose an install folder, confirm an uninstall, or a
// plain message with a Close button.
internal static class SetupWindow
{
    private const string ClassName = "ShhtartupSetupWindowClass";

    // Layout in device-independent pixels.
    private const int ClientWDip = 480;
    private const int InstallHDip = 356;
    private const int ConfirmHDip = 204;
    private const int MessageHDip = 184;
    private const int PadDip = 24;
    private const int GlyphYDip = 22;
    private const int GlyphWDip = 24;
    private const int TitleXDip = 58;
    private const int TitleHDip = 28;
    private const int TextYDip = 62;
    private const int TextHDip = 60;
    private const int LabelYDip = 112;
    private const int PathYDip = 132;
    private const int ControlHDip = 32;
    private const int CheckYDip = 182;
    private const int CheckStepDip = 30;
    private const int CheckBoxDip = 20;
    private const int CheckLabelXDip = 32;
    private const int CheckRowWDip = 320;
    private const int CheckNoteHDip = 18;
    private const int ErrorYDip = 260;
    private const int ErrorHDip = 38;
    private const int BrowseWDip = 96;
    private const int PrimaryWDip = 112;
    private const int SecondaryWDip = 96;
    private const int GapDip = 8;

    private const char GlyphDownload = '\uE896';
    private const char GlyphWarning = '\uE7BA';
    private const char GlyphDone = '\uE73E';
    private const char GlyphError = '\uE783';

    private enum Mode { Install, Confirm, Message }
    private enum Part { None, PathBox, Browse, DesktopShortcut, StartWithWindows, Primary, Secondary }

    private static bool _registered;
    private static nint _hwnd;
    private static UiFonts? _fonts;
    private static bool _open;
    private static Mode _mode;
    private static Part _hover;
    private static Part _pressed;
    private static bool _trackingLeave;

    private static string _title = string.Empty;
    private static string _text = string.Empty;
    private static char _glyph;
    private static bool _success;
    private static string _path = string.Empty;
    private static string? _error;
    private static bool _accepted;
    private static bool _desktopShortcut;
    private static bool _startWithWindows;

    // Returns the folder Shhtartup was installed to, or null if the user cancelled.
    public static string? RunInstall(string defaultFolder)
    {
        _title = "Install Shhtartup";
        _text = "Choose where to install Shhtartup.";
        _glyph = GlyphDownload;
        _path = defaultFolder;
        _desktopShortcut = true;
        _startWithWindows = true;
        _error = null;
        RunModal(Mode.Install, "Shhtartup Setup", InstallHDip);
        return _accepted ? _path : null;
    }

    public static bool ConfirmUninstall()
    {
        _title = "Uninstall Shhtartup?";
        _text = "This removes Shhtartup along with ALL of its settings. This action cannot be undone.";
        _glyph = GlyphWarning;
        RunModal(Mode.Confirm, "Uninstall Shhtartup", ConfirmHDip);
        return _accepted;
    }

    public static void ShowMessage(string title, string text, bool success = false)
    {
        _title = title;
        _text = text;
        _success = success;
        _glyph = success ? GlyphDone : GlyphError;
        RunModal(Mode.Message, "Shhtartup", MessageHDip);
    }

    private static void RunModal(Mode mode, string caption, int clientHDip)
    {
        _mode = mode;
        _accepted = false;
        _hover = Part.None;
        _pressed = Part.None;
        Theme.Refresh();
        Create(caption, clientHDip);

        _open = true;
        while (_open && User32.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            User32.TranslateMessage(in msg);
            User32.DispatchMessageW(in msg);
        }
    }

    private static void Create(string caption, int clientHDip)
    {
        var hInstance = User32.GetModuleHandleW(null);

        if (!_registered)
        {
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
            _registered = true;
        }

        // Centred on the monitor the user is working on, sized for its DPI.
        var (monitor, dpi) = User32.MonitorAtCursor();
        _fonts?.Dispose();
        _fonts = new UiFonts(dpi);

        const uint style = User32.WS_OVERLAPPED | User32.WS_CAPTION | User32.WS_SYSMENU;
        var frame = new RECT { Right = _fonts.Px(ClientWDip), Bottom = _fonts.Px(clientHDip) };
        User32.AdjustWindowRectExForDpi(ref frame, style, false, 0, dpi);
        var w = frame.Right - frame.Left;
        var h = frame.Bottom - frame.Top;
        var work = monitor.rcWork;
        var x = work.Left + (work.Right - work.Left - w) / 2;
        var y = work.Top + (work.Bottom - work.Top - h) / 2;

        _hwnd = User32.CreateWindowExW(0, ClassName, caption, style, x, y, w, h, 0, 0, hInstance, 0);
        Theme.ApplyFrame(_hwnd, isPopup: false);
        User32.ShowWindow(_hwnd, User32.SW_SHOW);
        User32.SetForegroundWindow(_hwnd);
    }

    private static void Close(bool accepted)
    {
        _accepted = accepted;
        _open = false;
        User32.DestroyWindow(_hwnd);
        _hwnd = 0;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case User32.WM_PAINT:
                Paint(hWnd);
                return 0;

            case User32.WM_ERASEBKGND:
                return 1;

            case User32.WM_MOUSEMOVE:
                OnMouseMove(hWnd, MouseX(lParam), MouseY(lParam));
                return 0;

            case User32.WM_MOUSELEAVE:
                _trackingLeave = false;
                SetHover(hWnd, Part.None);
                return 0;

            case User32.WM_LBUTTONDOWN:
                _pressed = HitTest(hWnd, MouseX(lParam), MouseY(lParam));
                User32.InvalidateRect(hWnd, 0, false);
                return 0;

            case User32.WM_LBUTTONUP:
                OnMouseUp(hWnd, MouseX(lParam), MouseY(lParam));
                return 0;

            case User32.WM_KEYDOWN:
                if ((int)wParam == User32.VK_RETURN)
                {
                    Activate(hWnd, Part.Primary);
                }
                else if ((int)wParam == User32.VK_ESCAPE)
                {
                    Close(accepted: false);
                }
                return 0;

            case User32.WM_DPICHANGED:
                OnDpiChanged(hWnd, (uint)(wParam & 0xFFFF), lParam);
                return 0;

            case User32.WM_SETTINGCHANGE:
            case User32.WM_DWMCOLORIZATIONCOLORCHANGED:
                Theme.Refresh();
                Theme.ApplyFrame(hWnd, isPopup: false);
                User32.InvalidateRect(hWnd, 0, false);
                return 0;

            case User32.WM_CLOSE:
                Close(accepted: false);
                return 0;
        }

        return User32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static int MouseX(nint lParam) => (short)(lParam & 0xFFFF);
    private static int MouseY(nint lParam) => (short)((lParam >> 16) & 0xFFFF);

    // --- Layout.

    private static (int W, int H) ClientSize(nint hwnd)
    {
        User32.GetClientRect(hwnd, out var rc);
        return (rc.Right, rc.Bottom);
    }

    private static UiRect PathRect(UiFonts f, int w) =>
        new(f.Px(PadDip), f.Px(PathYDip), w - 2 * f.Px(PadDip) - f.Px(GapDip) - f.Px(BrowseWDip), f.Px(ControlHDip));

    private static UiRect BrowseRect(UiFonts f, int w) =>
        new(w - f.Px(PadDip) - f.Px(BrowseWDip), f.Px(PathYDip), f.Px(BrowseWDip), f.Px(ControlHDip));

    // A checkbox and its label; the whole row is clickable, like in Windows.
    private static UiRect CheckRowRect(UiFonts f, int index) =>
        new(f.Px(PadDip), f.Px(CheckYDip + index * CheckStepDip), f.Px(CheckRowWDip), f.Px(CheckBoxDip));

    // Footer buttons, right-aligned: [Primary] [Secondary]. Message mode only has the one (Primary).
    private static UiRect SecondaryRect(UiFonts f, int w, int h) =>
        new(w - f.Px(PadDip) - f.Px(SecondaryWDip), h - f.Px(PadDip) - f.Px(ControlHDip), f.Px(SecondaryWDip), f.Px(ControlHDip));

    private static UiRect PrimaryRect(UiFonts f, int w, int h)
    {
        if (_mode == Mode.Message)
        {
            return SecondaryRect(f, w, h);
        }
        var secondary = SecondaryRect(f, w, h);
        return secondary with { X = secondary.X - f.Px(GapDip) - f.Px(PrimaryWDip), W = f.Px(PrimaryWDip) };
    }

    private static Part HitTest(nint hwnd, int x, int y)
    {
        var f = _fonts!;
        var (w, h) = ClientSize(hwnd);
        if (PrimaryRect(f, w, h).Contains(x, y)) return Part.Primary;
        if (_mode != Mode.Message && SecondaryRect(f, w, h).Contains(x, y)) return Part.Secondary;
        if (_mode == Mode.Install && BrowseRect(f, w).Contains(x, y)) return Part.Browse;
        if (_mode == Mode.Install && PathRect(f, w).Contains(x, y)) return Part.PathBox;
        if (_mode == Mode.Install && CheckRowRect(f, 0).Contains(x, y)) return Part.DesktopShortcut;
        if (_mode == Mode.Install && CheckRowRect(f, 1).Contains(x, y)) return Part.StartWithWindows;
        return Part.None;
    }

    // --- Input.

    private static void SetHover(nint hwnd, Part part)
    {
        if (_hover != part)
        {
            _hover = part;
            User32.InvalidateRect(hwnd, 0, false);
        }
    }

    private static void OnMouseMove(nint hwnd, int x, int y)
    {
        if (!_trackingLeave)
        {
            var tme = new TRACKMOUSEEVENT
            {
                cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = User32.TME_LEAVE,
                hwndTrack = hwnd,
            };
            User32.TrackMouseEvent(ref tme);
            _trackingLeave = true;
        }
        SetHover(hwnd, HitTest(hwnd, x, y));
    }

    private static void OnMouseUp(nint hwnd, int x, int y)
    {
        var released = HitTest(hwnd, x, y);
        var pressed = _pressed;
        _pressed = Part.None;
        User32.InvalidateRect(hwnd, 0, false);

        if (released == pressed)
        {
            Activate(hwnd, released);
        }
    }

    private static void Activate(nint hwnd, Part part)
    {
        switch (part)
        {
            case Part.PathBox:
            case Part.Browse:
                Browse(hwnd);
                break;

            case Part.DesktopShortcut:
                _desktopShortcut = !_desktopShortcut;
                User32.InvalidateRect(hwnd, 0, false);
                break;

            case Part.StartWithWindows:
                _startWithWindows = !_startWithWindows;
                User32.InvalidateRect(hwnd, 0, false);
                break;

            case Part.Primary when _mode == Mode.Install:
                Install(hwnd);
                break;

            case Part.Primary:
                Close(accepted: true);
                break;

            case Part.Secondary:
                Close(accepted: false);
                break;
        }
    }

    private static void Browse(nint hwnd)
    {
        var picked = ShellHelpers.PickFolder(hwnd, "Choose where to install Shhtartup", _path);
        if (picked is null)
        {
            return;
        }

        // Picking e.g. D:\Apps installs to D:\Apps\Shhtartup, like most installers.
        _path = Path.GetFileName(picked.TrimEnd('\\')).Equals("Shhtartup", StringComparison.OrdinalIgnoreCase)
            ? picked
            : Path.Combine(picked, "Shhtartup");
        _error = null;
        User32.InvalidateRect(hwnd, 0, false);
    }

    private static void Install(nint hwnd)
    {
        _error = Path.IsPathFullyQualified(_path)
            ? Installer.InstallTo(_path, new InstallOptions(_desktopShortcut, _startWithWindows))
            : "Choose a folder to install to.";
        if (_error is null)
        {
            Close(accepted: true);
            return;
        }
        User32.InvalidateRect(hwnd, 0, false);
    }

    private static void OnDpiChanged(nint hwnd, uint dpi, nint suggestedRect)
    {
        var old = _fonts;
        _fonts = new UiFonts(dpi);
        unsafe
        {
            var r = *(RECT*)suggestedRect;
            User32.SetWindowPos(hwnd, 0, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }
        old?.Dispose();
        User32.InvalidateRect(hwnd, 0, false);
    }

    // --- Painting.

    private static void Paint(nint hwnd)
    {
        var hdc = User32.BeginPaint(hwnd, out var ps);
        var (w, h) = ClientSize(hwnd);
        PaintContent(hdc, _fonts!, w, h);
        User32.EndPaint(hwnd, in ps);
    }

    private static void PaintContent(nint hdc, UiFonts f, int w, int h)
    {
        var t = Theme.P;
        var pad = f.Px(PadDip);
        const uint wrap = User32.DT_LEFT | User32.DT_WORDBREAK;

        using var p = new Painter(hdc, w, h);
        p.FillRect(0, 0, w, h, t.WindowBg);

        var glyphColor = _mode switch
        {
            Mode.Confirm => Theme.IsDark ? Rgb.Hex(0xFCE100) : Rgb.Hex(0x9D5D00),
            Mode.Message when !_success => ErrorColor(),
            _ => t.Accent,
        };
        Fluent.Glyph(p, f.Icon, _glyph, glyphColor, new UiRect(pad, f.Px(GlyphYDip), f.Px(GlyphWDip), f.Px(TitleHDip)));
        p.Text(_title, f.BodyStrong, t.TextPrimary,
            new UiRect(f.Px(TitleXDip), f.Px(GlyphYDip), w - f.Px(TitleXDip) - pad, f.Px(TitleHDip)), Fluent.TextLeft);
        p.Text(_text, f.Body, t.TextSecondary, new UiRect(pad, f.Px(TextYDip), w - 2 * pad, f.Px(TextHDip)), wrap);

        if (_mode == Mode.Install)
        {
            p.Text("Install location", f.Caption, t.TextSecondary,
                new UiRect(pad, f.Px(LabelYDip), w - 2 * pad, f.Px(18)), Fluent.TextLeft);

            var box = PathRect(f, w);
            Fluent.InputFrame(p, f, box, focused: _hover == Part.PathBox);
            var inner = f.Px(10);
            var textRect = box with { X = box.X + inner, W = box.W - 2 * inner };
            p.Text(FitPath(p, f.Body, _path, textRect.W), f.Body, t.TextPrimary, textRect, Fluent.TextLeft);

            Fluent.Button(p, f, BrowseRect(f, w), "Browse…",
                hover: _hover == Part.Browse, pressed: _pressed == Part.Browse, enabled: true);

            DrawCheckRow(p, f, 0, "Create desktop shortcut", _desktopShortcut, Part.DesktopShortcut);
            DrawCheckRow(p, f, 1, "Start Shhtartup on startup", _startWithWindows, Part.StartWithWindows);
            var startupRow = CheckRowRect(f, 1);
            p.Text("You can adjust this any time in the Settings menu", f.Caption, t.TextSecondary,
                new UiRect(startupRow.X + f.Px(CheckLabelXDip), startupRow.Bottom + f.Px(2), w - startupRow.X - f.Px(CheckLabelXDip) - pad,
                    f.Px(CheckNoteHDip)), Fluent.TextLeft);

            if (_error is not null)
            {
                p.Text(_error, f.Caption, ErrorColor(), new UiRect(pad, f.Px(ErrorYDip), w - 2 * pad, f.Px(ErrorHDip)), wrap);
            }
        }

        var primaryLabel = _mode switch
        {
            Mode.Install => "Install",
            Mode.Confirm => "Uninstall",
            _ => "Close",
        };
        Fluent.AccentButton(p, f, PrimaryRect(f, w, h), primaryLabel,
            hover: _hover == Part.Primary, pressed: _pressed == Part.Primary);

        if (_mode != Mode.Message)
        {
            Fluent.Button(p, f, SecondaryRect(f, w, h), "Cancel",
                hover: _hover == Part.Secondary, pressed: _pressed == Part.Secondary, enabled: true);
        }
    }

    private static void DrawCheckRow(Painter p, UiFonts f, int index, string label, bool isChecked, Part part)
    {
        var row = CheckRowRect(f, index);
        var box = row with { W = f.Px(CheckBoxDip) };
        Fluent.CheckBox(p, f, box, isChecked, hover: _hover == part, surface: Theme.P.WindowBg);
        p.Text(label, f.Body, Theme.P.TextPrimary,
            row with { X = row.X + f.Px(CheckLabelXDip), W = row.W - f.Px(CheckLabelXDip) }, Fluent.TextLeft);
    }

    // Shortens a long path from the middle so the end stays visible: C:\…\Programs\Shhtartup.
    // (DrawText's DT_PATH_ELLIPSIS doesn't shorten these paths, so it's done by measuring.)
    private static string FitPath(Painter p, nint font, string path, int maxWidth)
    {
        if (p.MeasureText(path, font) <= maxWidth)
        {
            return path;
        }

        var parts = path.Split('\\');
        for (var keep = parts.Length - 2; keep >= 1; keep--)
        {
            var candidate = parts[0] + "\\…\\" + string.Join("\\", parts[^keep..]);
            if (p.MeasureText(candidate, font) <= maxWidth)
            {
                return candidate;
            }
        }
        return "…\\" + parts[^1];
    }

    // WinUI's critical (error) text colour.
    private static Rgb ErrorColor() => Theme.IsDark ? Rgb.Hex(0xFF99A4) : Rgb.Hex(0xC42B1C);
}
