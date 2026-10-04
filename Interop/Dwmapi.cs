using System.Runtime.InteropServices;

namespace Tinnitdown.Interop;

internal static partial class Dwmapi
{
    // Dark title bar. 20 on Windows 10 20H1+ and 11; 19 on earlier Windows 10 builds.
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;

    // Windows 11 only (ignored on 10).
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint DWMWA_BORDER_COLOR = 34;
    public const int DWMWCP_ROUND = 2;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);
}
