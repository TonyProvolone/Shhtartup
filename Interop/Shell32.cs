using System.Runtime.InteropServices;

namespace Tinnitdown.Interop;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATAW
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
    public uint dwState;
    public uint dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string szInfo;
    public uint uVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

internal static partial class Shell32
{
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    // Required under NOTIFYICON_VERSION_4 to show the standard tooltip (otherwise it's suppressed
    // in favour of an app-drawn UI). https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-notifyicondataw
    public const uint NIF_SHOWTIP = 0x00000080;
    public const uint NIF_INFO = 0x00000010;

    public const uint NIIF_INFO = 0x00000001;
    public const uint NIIF_WARNING = 0x00000002;

    // Sent to the tray callback (low word of lParam under version 4) when a notification is clicked.
    public const uint NIN_BALLOONUSERCLICK = 0x0405;

    public const uint NOTIFYICON_VERSION_4 = 4;

    // SHQueryUserNotificationState results that mean "don't pop anything up right now".
    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;

    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out int pquns);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out IShellItem ppv);

    // NOTIFYICONDATAW contains fixed-size marshalled string fields, which source-generated
    // LibraryImport cannot marshal when nested in a struct (SYSLIB1051) -- classic DllImport
    // marshalling (ILC-generated, not reflection-based) still works under Native AOT.
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);
}
