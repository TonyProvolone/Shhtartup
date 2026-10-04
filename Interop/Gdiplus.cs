using System.Runtime.InteropServices;

namespace Tinnitdown.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct GdiplusStartupInput
{
    public uint GdiplusVersion;
    public nint DebugEventCallback;
    public int SuppressBackgroundThread;
    public int SuppressExternalCodecs;
}

// GDI+ flat API -- used only for anti-aliased shapes (rounded rects, circles). Plain GDI can't
// anti-alias, which is what makes classic Win32 UI look jagged.
internal static partial class Gdiplus
{
    public const int SmoothingModeAntiAlias = 4;
    public const int PixelOffsetModeHalf = 4;
    public const int FillModeAlternate = 0;
    public const int FlushIntentionSync = 1;

    [LibraryImport("gdiplus.dll")]
    public static partial int GdiplusStartup(out nuint token, in GdiplusStartupInput input, nint output);

    [LibraryImport("gdiplus.dll")]
    public static partial void GdiplusShutdown(nuint token);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateFromHDC(nint hdc, out nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteGraphics(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetSmoothingMode(nint graphics, int smoothingMode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPixelOffsetMode(nint graphics, int pixelOffsetMode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFlush(nint graphics, int intention);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateSolidFill(uint argb, out nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteBrush(nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillRectangle(nint graphics, nint brush, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillEllipse(nint graphics, nint brush, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePath(int fillMode, out nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePath(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathArc(nint path, float x, float y, float width, float height, float startAngle, float sweepAngle);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipClosePathFigure(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillPath(nint graphics, nint brush, nint path);
}
