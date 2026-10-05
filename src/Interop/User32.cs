using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace Shhtartup.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nuint wParam;
    public nint lParam;
    public uint time;
    public int ptX;
    public int ptY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? lpszMenuName;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal unsafe struct PAINTSTRUCT
{
    public nint hdc;
    public int fErase;
    public RECT rcPaint;
    public int fRestore;
    public int fIncUpdate;
    public fixed byte rgbReserved[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct TRACKMOUSEEVENT
{
    public uint cbSize;
    public uint dwFlags;
    public nint hwndTrack;
    public uint dwHoverTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

internal static partial class User32
{
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_HSCROLL = 0x0114;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_APP = 0x8000;

    public const nint HWND_MESSAGE = -3;

    public const uint MF_STRING = 0x00000000;
    public const uint MF_CHECKED = 0x00000008;
    public const uint MF_UNCHECKED = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_POPUP = 0x00000010;
    public const uint MF_BYPOSITION = 0x00000400;

    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;

    public const int IDI_APPLICATION = 32512;

    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_BORDER = 0x00800000;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CLIPCHILDREN = 0x02000000;

    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    public const uint WM_PAINT = 0x000F;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_MOUSELEAVE = 0x02A3;

    public const nint HWND_TOPMOST = -1;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int COLOR_MENU = 4;
    public const int COLOR_MENUTEXT = 7;
    public const int COLOR_HIGHLIGHT = 13;
    public const int COLOR_HIGHLIGHTTEXT = 14;
    public const int COLOR_BTNFACE = 15;
    public const int COLOR_3DSHADOW = 16;

    public const uint DT_LEFT = 0x0000;
    public const uint DT_CENTER = 0x0001;
    public const uint DT_RIGHT = 0x0002;
    public const uint DT_VCENTER = 0x0004;
    public const uint DT_SINGLELINE = 0x0020;

    public const uint TME_LEAVE = 0x00000002;

    public const uint DT_NOPREFIX = 0x0800;
    public const uint DT_END_ELLIPSIS = 0x8000;

    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_TABSTOP = 0x00010000;
    public const uint CS_DROPSHADOW = 0x00020000;
    public const int IDC_ARROW = 32512;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_CTLCOLOREDIT = 0x0133;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

    public const int VK_ESCAPE = 0x1B;
    public const int VK_PRIOR = 0x21;
    public const int VK_NEXT = 0x22;
    public const int VK_END = 0x23;
    public const int VK_HOME = 0x24;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;

    // Edit control.
    public const uint ES_RIGHT = 0x0002;
    public const uint ES_NUMBER = 0x2000;
    public const uint EM_SETSEL = 0x00B1;
    public const uint EM_LIMITTEXT = 0x00C5;
    public const uint EM_SETMARGINS = 0x00D3;
    public const uint EC_LEFTMARGIN = 0x0001;
    public const uint EC_RIGHTMARGIN = 0x0002;
    public const int EN_SETFOCUS = 0x0100;
    public const int EN_KILLFOCUS = 0x0200;
    public const int EN_CHANGE = 0x0300;

    // WNDCLASSEXW contains marshalled string fields, which source-generated LibraryImport
    // cannot marshal when nested in a struct (SYSLIB1051) -- classic DllImport marshalling
    // (ILC-generated, not reflection-based) still works fine for this under Native AOT.
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(in WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    public static partial int TranslateMessage(in MSG lpMsg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(in MSG lpMsg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    [LibraryImport("user32.dll")]
    public static partial int KillTimer(nint hWnd, nuint uIDEvent);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    public static partial nint LoadIconW(nint hInstance, nint lpIconName);

    public const int SM_CXSMICON = 49;
    public const uint LR_DEFAULTCOLOR = 0x00000000;

    // One image from an .ico file (PNG or DIB data) as an icon handle. dwVer must be 0x00030000.
    [LibraryImport("user32.dll")]
    public static unsafe partial nint CreateIconFromResourceEx(byte* presbits, uint dwResSize, [MarshalAs(UnmanagedType.Bool)] bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    public const uint DI_NORMAL = 0x0003;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIconEx(nint hdc, int xLeft, int yTop, nint hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, nint hbrFlickerFreeDraw, uint diFlags);

    // Click-through overlays: WS_EX_LAYERED + WS_EX_TRANSPARENT let mouse input fall through to
    // whatever is underneath, even in other processes.
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint LWA_ALPHA = 0x00000002;
    public const uint WM_NCHITTEST = 0x0084;
    public const nint HTTRANSPARENT = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    // Takes ownership of hRgn.
    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll")]
    public static partial int DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll")]
    public static partial int SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lptpm);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll")]
    public static partial int PostMessageW(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(nint hWnd, string lpString);

    // StringBuilder "out buffer" marshalling isn't supported by source-generated LibraryImport --
    // classic DllImport marshalling (ILC-generated, not reflection-based) still works under Native AOT.
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsDialogMessageW(nint hDlg, in MSG lpMsg);

    public const uint WM_SETFONT = 0x0030;

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    public static partial int FillRect(nint hDC, in RECT lprc, nint hbr);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawTextW(nint hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    [LibraryImport("user32.dll")]
    public static partial uint GetSysColor(int nIndex);

    [LibraryImport("user32.dll")]
    public static partial nint GetSysColorBrush(int nIndex);

    [LibraryImport("user32.dll")]
    public static partial int InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    // PAINTSTRUCT contains a fixed-size reserved buffer; classic DllImport handles the blittable
    // struct cleanly (ILC-generated under Native AOT) without source-generator edge cases.
    [DllImport("user32.dll")]
    public static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndPaint(nint hWnd, in PAINTSTRUCT lpPaint);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursorW(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    public static partial nint SetCapture(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowExW(nint hWndParent, nint hWndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    public const uint DT_PATH_ELLIPSIS = 0x4000;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    public const uint DT_WORDBREAK = 0x0010;
    public const uint DT_CALCRECT = 0x0400;
    public const int VK_RETURN = 0x0D;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustWindowRectExForDpi(ref RECT lpRect, uint dwStyle, [MarshalAs(UnmanagedType.Bool)] bool bMenu, uint dwExStyle, uint dpi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessDpiAwarenessContext(nint value);

    // Monitor under the cursor, its work area (excludes the taskbar) and its DPI.
    public static (MONITORINFO Info, uint Dpi) MonitorAtCursor()
    {
        GetCursorPos(out var pt);
        return MonitorInfo(MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST));
    }

    // Monitor showing the active window (e.g. the game that just started), else the primary one.
    public static (MONITORINFO Info, uint Dpi) MonitorOfForegroundWindow()
    {
        var hwnd = GetForegroundWindow();
        return hwnd == 0 ? PrimaryMonitor() : MonitorOfWindow(hwnd);
    }

    public static (MONITORINFO Info, uint Dpi) MonitorOfWindow(nint hwnd) =>
        MonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    // The primary monitor always contains (0, 0).
    public static (MONITORINFO Info, uint Dpi) PrimaryMonitor() =>
        MonitorInfo(MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY));

    private static (MONITORINFO Info, uint Dpi) MonitorInfo(nint monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref info);
        if (Shcore.GetDpiForMonitor(monitor, Shcore.MDT_EFFECTIVE_DPI, out var dpi, out _) != 0 || dpi == 0)
        {
            dpi = 96;
        }
        return (info, dpi);
    }
}
