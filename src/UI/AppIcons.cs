using Shhtartup.Interop;

namespace Shhtartup;

// The app's icons, from src\Assets (rebuilt from the SVGs there by scripts\build-icons.ps1).
internal static class AppIcons
{
    // The compiler embeds <ApplicationIcon> (AppIcon.ico) as the exe's icon group under this ID.
    private const int AppIconId = 32512;

    // For window classes. Windows takes the small title-bar size from the same icon group.
    public static nint App => User32.LoadIconW(User32.GetModuleHandleW(null), AppIconId);

    // Tray icon size in pixels for the current display scaling.
    public static int TraySize => User32.GetSystemMetricsForDpi(User32.SM_CXSMICON, User32.GetDpiForSystem());

    // The tray glyph: black for a light taskbar, white for a dark one. The caller owns the handle
    // (DestroyIcon it once the tray has been given a newer one).
    public static nint CreateTrayIcon(bool lightTaskbar, int size)
    {
        var name = lightTaskbar ? "TrayLight.ico" : "TrayDark.ico";
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            return User32.LoadIconW(0, User32.IDI_APPLICATION);
        }

        var ico = new byte[stream.Length];
        stream.ReadExactly(ico);
        var (offset, length) = PickImage(ico, size);

        unsafe
        {
            fixed (byte* image = &ico[offset])
            {
                return User32.CreateIconFromResourceEx(image, (uint)length, true, 0x00030000, size, size, User32.LR_DEFAULTCOLOR);
            }
        }
    }

    // The smallest image in the .ico at least `size` pixels wide (so it's only ever scaled down),
    // or the largest one if none is big enough.
    private static (int Offset, int Length) PickImage(byte[] ico, int size)
    {
        var count = BitConverter.ToUInt16(ico, 4);
        int best = -1, bestWidth = 0;
        for (var i = 0; i < count; i++)
        {
            var width = ico[6 + 16 * i] == 0 ? 256 : ico[6 + 16 * i]; // 0 means 256
            var better = best < 0
                || (width >= size && (bestWidth < size || width < bestWidth))
                || (width < size && bestWidth < size && width > bestWidth);
            if (better)
            {
                best = i;
                bestWidth = width;
            }
        }

        var entry = 6 + 16 * best;
        return ((int)BitConverter.ToUInt32(ico, entry + 12), (int)BitConverter.ToUInt32(ico, entry + 8));
    }
}
