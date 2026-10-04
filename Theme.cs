using Microsoft.Win32;
using Tinnitdown.Interop;

namespace Tinnitdown;

internal readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Hex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    // GDI wants 0x00BBGGRR; GDI+ wants 0xAARRGGBB.
    public uint ColorRef => (uint)(R | (G << 8) | (B << 16));
    public uint Argb => 0xFF000000u | ((uint)R << 16) | ((uint)G << 8) | B;

    public Rgb Mix(Rgb other, float amount) => new(
        (byte)(R + (other.R - R) * amount),
        (byte)(G + (other.G - G) * amount),
        (byte)(B + (other.B - B) * amount));
}

// Windows 11 (Fluent / WinUI) colour tokens, flattened onto solid surfaces.
internal sealed class Palette
{
    public required Rgb WindowBg { get; init; }
    public required Rgb CardBg { get; init; }
    public required Rgb CardBorder { get; init; }
    public required Rgb FlyoutBg { get; init; }
    public required Rgb FlyoutBorder { get; init; }
    public required Rgb Divider { get; init; }
    public required Rgb SubtleHover { get; init; }
    public required Rgb SubtlePressed { get; init; }
    public required Rgb TextPrimary { get; init; }
    public required Rgb TextSecondary { get; init; }
    public required Rgb TextDisabled { get; init; }
    public required Rgb Accent { get; init; }
    public required Rgb AccentHover { get; init; }
    public required Rgb TextOnAccent { get; init; }
    public required Rgb SliderRail { get; init; }
    public required Rgb ThumbFill { get; init; }
    public required Rgb ThumbBorder { get; init; }
    public required Rgb ToggleOffBorder { get; init; }
    public required Rgb ToggleOffKnob { get; init; }
    public required Rgb ControlFill { get; init; }
    public required Rgb ControlFillHover { get; init; }
    public required Rgb ControlFillPressed { get; init; }
    public required Rgb ControlFillDisabled { get; init; }
    public required Rgb ControlBorder { get; init; }
    public required Rgb ControlBorderBottom { get; init; }
    public required Rgb InputBorderBottom { get; init; }
    public required Rgb InputFillFocused { get; init; }
}

internal static class Theme
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AccentKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    public static bool IsWindows11 { get; } = Environment.OSVersion.Version.Build >= 22000;

    public static string TextFace => IsWindows11 ? "Segoe UI Variable Text" : "Segoe UI";
    public static string IconFace => IsWindows11 ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static bool IsDark { get; private set; }

    public static Palette P { get; private set; } = Build(dark: false, Rgb.Hex(0x005FB8));

    // Re-reads light/dark mode and the accent colour. Cheap; called whenever a window is shown and
    // when Windows broadcasts a theme or colour change. forceDark overrides the system setting (used
    // to render previews of both themes).
    public static void Refresh(bool? forceDark = null)
    {
        try
        {
            IsDark = forceDark ?? (Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is int light && light == 0);
        }
        catch
        {
            IsDark = false;
        }

        P = Build(IsDark, ReadAccent(IsDark));
    }

    // Dark title bar to match the theme; on Windows 11 also rounded corners and a theme-coloured border
    // for popups (normal windows are rounded by Windows automatically).
    public static void ApplyFrame(nint hwnd, bool isPopup)
    {
        var dark = IsDark ? 1 : 0;
        if (Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
        {
            Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref dark, sizeof(int));
        }

        if (isPopup && IsWindows11)
        {
            var round = Dwmapi.DWMWCP_ROUND;
            Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            var border = unchecked((int)P.FlyoutBorder.ColorRef);
            Dwmapi.DwmSetWindowAttribute(hwnd, Dwmapi.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
    }

    // Windows stores 8 shades of the accent colour (light 3..1, base, dark 1..3). WinUI fills controls
    // with "dark 1" in light mode and "light 2" in dark mode, for contrast against the surface.
    private static Rgb ReadAccent(bool dark)
    {
        try
        {
            if (Registry.GetValue(AccentKey, "AccentPalette", null) is byte[] { Length: >= 32 } palette)
            {
                var i = (dark ? 1 : 4) * 4;
                return new Rgb(palette[i], palette[i + 1], palette[i + 2]);
            }
        }
        catch
        {
        }

        return dark ? Rgb.Hex(0x60CDFF) : Rgb.Hex(0x005FB8);
    }

    private static Palette Build(bool dark, Rgb accent)
    {
        Rgb C(uint light, uint darkValue) => Rgb.Hex(dark ? darkValue : light);
        var windowBg = C(0xF3F3F3, 0x202020);

        return new Palette
        {
            WindowBg = windowBg,
            CardBg = C(0xFBFBFB, 0x2B2B2B),
            CardBorder = C(0xE5E5E5, 0x1D1D1D),
            FlyoutBg = C(0xF9F9F9, 0x2C2C2C),
            FlyoutBorder = C(0xD5D5D5, 0x3F3F3F),
            Divider = C(0xE5E5E5, 0x3D3D3D),
            SubtleHover = C(0xEDEDED, 0x383838),
            SubtlePressed = C(0xF1F1F1, 0x333333),
            TextPrimary = C(0x1B1B1B, 0xFFFFFF),
            TextSecondary = C(0x616161, 0xC8C8C8),
            TextDisabled = C(0xA0A0A0, 0x717171),
            Accent = accent,
            AccentHover = accent.Mix(windowBg, 0.1f),
            TextOnAccent = C(0xFFFFFF, 0x000000),
            SliderRail = C(0x8A8A8A, 0x9F9F9F),
            ThumbFill = C(0xFFFFFF, 0x454545),
            ThumbBorder = C(0xD6D6D6, 0x555555),
            ToggleOffBorder = C(0x8A8A8A, 0x9F9F9F),
            ToggleOffKnob = C(0x5F5F5F, 0xCFCFCF),
            ControlFill = C(0xFEFEFE, 0x373737),
            ControlFillHover = C(0xF6F6F6, 0x3C3C3C),
            ControlFillPressed = C(0xF3F3F3, 0x323232),
            ControlFillDisabled = C(0xF7F7F7, 0x303030),
            ControlBorder = C(0xE3E3E3, 0x434343),
            ControlBorderBottom = C(0xC9C9C9, 0x3A3A3A),
            InputBorderBottom = C(0x8A8A8A, 0x9F9F9F),
            InputFillFocused = C(0xFFFFFF, 0x1F1F1F),
        };
    }
}
