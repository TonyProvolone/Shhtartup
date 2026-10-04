using Tinnitdown.Interop;

namespace Tinnitdown;

internal readonly record struct UiRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public bool Contains(int px, int py) => px >= X && px < Right && py >= Y && py < Bottom;
}

// The Windows 11 type ramp (Segoe UI Variable on 11, Segoe UI on 10) at one DPI. Layout is written in
// device-independent pixels (96 DPI) and converted with Px(), so the UI is the right size at any
// display scaling.
internal sealed class UiFonts : IDisposable
{
    public UiFonts(uint dpi)
    {
        Dpi = dpi == 0 ? 96 : dpi;
        Body = Create(14, Gdi32.FW_NORMAL, Theme.TextFace);
        BodyStrong = Create(14, Gdi32.FW_SEMIBOLD, Theme.TextFace);
        Caption = Create(12, Gdi32.FW_NORMAL, Theme.TextFace);
        Icon = Create(16, Gdi32.FW_NORMAL, Theme.IconFace);
        IconSmall = Create(10, Gdi32.FW_NORMAL, Theme.IconFace);
    }

    public uint Dpi { get; }
    public nint Body { get; }
    public nint BodyStrong { get; }
    public nint Caption { get; }
    public nint Icon { get; }
    public nint IconSmall { get; }

    public int Px(float dip) => (int)MathF.Round(dip * Dpi / 96f);

    public UiRect Rect(int x, int y, int w, int h) => new(Px(x), Px(y), Px(w), Px(h));

    // A border that stays crisp: 1px at 100%, 2px at 200%.
    public int Hairline => Math.Max(1, Px(1));

    private nint Create(float sizeDip, int weight, string face) =>
        Gdi32.CreateFontW(-Px(sizeDip), 0, 0, 0, weight, 0, 0, 0,
            Gdi32.DEFAULT_CHARSET, 0, 0, Gdi32.CLEARTYPE_QUALITY, 0, face);

    public void Dispose()
    {
        Gdi32.DeleteObject(Body);
        Gdi32.DeleteObject(BodyStrong);
        Gdi32.DeleteObject(Caption);
        Gdi32.DeleteObject(Icon);
        Gdi32.DeleteObject(IconSmall);
    }
}

// Double-buffered painting: everything is drawn into an off-screen bitmap and copied to the window in
// one blit (no flicker). Shapes go through GDI+ for anti-aliasing; text through GDI for ClearType.
internal sealed class Painter : IDisposable
{
    private readonly nint _target;
    private readonly nint _dc;
    private readonly nint _bitmap;
    private readonly nint _oldBitmap;
    private readonly nint _graphics;
    private readonly int _width;
    private readonly int _height;

    public Painter(nint targetDc, int width, int height)
    {
        _target = targetDc;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _dc = Gdi32.CreateCompatibleDC(targetDc);
        _bitmap = Gdi32.CreateCompatibleBitmap(targetDc, _width, _height);
        _oldBitmap = Gdi32.SelectObject(_dc, _bitmap);
        Gdi32.SetBkMode(_dc, Gdi32.TRANSPARENT);

        Gdiplus.GdipCreateFromHDC(_dc, out _graphics);
        Gdiplus.GdipSetSmoothingMode(_graphics, Gdiplus.SmoothingModeAntiAlias);
        Gdiplus.GdipSetPixelOffsetMode(_graphics, Gdiplus.PixelOffsetModeHalf);
    }

    public void FillRect(float x, float y, float w, float h, Rgb color)
    {
        Gdiplus.GdipCreateSolidFill(color.Argb, out var brush);
        Gdiplus.GdipFillRectangle(_graphics, brush, x, y, w, h);
        Gdiplus.GdipDeleteBrush(brush);
    }

    public void FillRoundRect(float x, float y, float w, float h, float radius, Rgb color)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        radius = MathF.Min(radius, MathF.Min(w, h) / 2);
        if (radius <= 0.5f)
        {
            FillRect(x, y, w, h, color);
            return;
        }

        var d = radius * 2;
        Gdiplus.GdipCreatePath(Gdiplus.FillModeAlternate, out var path);
        Gdiplus.GdipAddPathArc(path, x, y, d, d, 180, 90);
        Gdiplus.GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
        Gdiplus.GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
        Gdiplus.GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
        Gdiplus.GdipClosePathFigure(path);

        Gdiplus.GdipCreateSolidFill(color.Argb, out var brush);
        Gdiplus.GdipFillPath(_graphics, brush, path);
        Gdiplus.GdipDeleteBrush(brush);
        Gdiplus.GdipDeletePath(path);
    }

    public void FillCircle(float cx, float cy, float radius, Rgb color)
    {
        Gdiplus.GdipCreateSolidFill(color.Argb, out var brush);
        Gdiplus.GdipFillEllipse(_graphics, brush, cx - radius, cy - radius, radius * 2, radius * 2);
        Gdiplus.GdipDeleteBrush(brush);
    }

    public void Text(string text, nint font, Rgb color, UiRect rect, uint flags)
    {
        // GDI+ may batch; flush so text lands on top of shapes drawn before it.
        Gdiplus.GdipFlush(_graphics, Gdiplus.FlushIntentionSync);

        var oldFont = Gdi32.SelectObject(_dc, font);
        Gdi32.SetTextColor(_dc, color.ColorRef);
        var r = new RECT { Left = rect.X, Top = rect.Y, Right = rect.Right, Bottom = rect.Bottom };
        User32.DrawTextW(_dc, text, text.Length, ref r, flags | User32.DT_NOPREFIX);
        Gdi32.SelectObject(_dc, oldFont);
    }

    public void Dispose()
    {
        Gdiplus.GdipDeleteGraphics(_graphics);
        Gdi32.BitBlt(_target, 0, 0, _width, _height, _dc, 0, 0, Gdi32.SRCCOPY);
        Gdi32.SelectObject(_dc, _oldBitmap);
        Gdi32.DeleteObject(_bitmap);
        Gdi32.DeleteDC(_dc);
    }
}

// Windows 11 (WinUI) control visuals, drawn by hand.
internal static class Fluent
{
    public const uint TextLeft = User32.DT_LEFT | User32.DT_VCENTER | User32.DT_SINGLELINE | User32.DT_END_ELLIPSIS;
    public const uint TextRight = User32.DT_RIGHT | User32.DT_VCENTER | User32.DT_SINGLELINE;
    public const uint TextCenter = User32.DT_CENTER | User32.DT_VCENTER | User32.DT_SINGLELINE;

    public const char GlyphVolume = '';
    public const char GlyphSettings = '';
    public const char GlyphPower = '';
    public const char GlyphUpdate = ''; // Sync
    public const char GlyphChevronUp = '';
    public const char GlyphChevronDown = '';

    public static void Card(Painter p, UiFonts f, UiRect r)
    {
        var t = Theme.P;
        var b = f.Hairline;
        var radius = f.Px(4);
        p.FillRoundRect(r.X, r.Y, r.W, r.H, radius, t.CardBorder);
        p.FillRoundRect(r.X + b, r.Y + b, r.W - 2 * b, r.H - 2 * b, radius - b, t.CardBg);
    }

    public static void Glyph(Painter p, nint font, char glyph, Rgb color, UiRect r) =>
        p.Text(glyph.ToString(), font, color, r, TextCenter);

    // Hover/pressed background for menu items and small icon buttons.
    public static void SubtleFill(Painter p, UiFonts f, UiRect r, bool hover, bool pressed)
    {
        if (pressed || hover)
        {
            p.FillRoundRect(r.X, r.Y, r.W, r.H, f.Px(4), pressed ? Theme.P.SubtlePressed : Theme.P.SubtleHover);
        }
    }

    // --- Slider: thin rail, accent fill up to the thumb, white thumb with an accent dot that grows on
    // hover and shrinks while dragging.

    private static float ThumbRadius(UiFonts f) => f.Px(10);

    public static int SliderValueAt(UiFonts f, UiRect r, int mouseX)
    {
        var thumb = ThumbRadius(f);
        var left = r.X + thumb;
        var right = r.Right - thumb;
        if (right <= left)
        {
            return 0;
        }
        return (int)Math.Clamp(MathF.Round((mouseX - left) / (right - left) * 100f), 0, 100);
    }

    public static void Slider(Painter p, UiFonts f, UiRect r, int value, bool hover, bool pressed)
    {
        var t = Theme.P;
        var thumb = ThumbRadius(f);
        var cy = r.Y + r.H / 2f;
        var rail = (float)f.Px(4);
        var thumbX = r.X + thumb + (r.W - 2 * thumb) * value / 100f;

        p.FillRoundRect(r.X, cy - rail / 2, r.W, rail, rail / 2, t.SliderRail);
        p.FillRoundRect(r.X, cy - rail / 2, thumbX - r.X, rail, rail / 2, t.Accent);

        p.FillCircle(thumbX, cy, thumb, t.ThumbBorder);
        p.FillCircle(thumbX, cy, thumb - f.Hairline, t.ThumbFill);
        var dot = pressed ? f.Px(5) : hover ? f.Px(7) : f.Px(6);
        p.FillCircle(thumbX, cy, dot, hover && !pressed ? t.AccentHover : t.Accent);
    }

    // --- Toggle switch (40x20).

    public static UiRect ToggleRect(UiFonts f, int x, int y) => new(x, y, f.Px(40), f.Px(20));

    public static void Toggle(Painter p, UiFonts f, UiRect r, bool on, bool hover, Rgb surface)
    {
        var t = Theme.P;
        var radius = r.H / 2f;
        var knob = hover ? f.Px(7) : f.Px(6);

        if (on)
        {
            p.FillRoundRect(r.X, r.Y, r.W, r.H, radius, hover ? t.AccentHover : t.Accent);
            p.FillCircle(r.Right - radius, r.Y + radius, knob, t.TextOnAccent);
        }
        else
        {
            var b = f.Hairline;
            p.FillRoundRect(r.X, r.Y, r.W, r.H, radius, t.ToggleOffBorder);
            p.FillRoundRect(r.X + b, r.Y + b, r.W - 2 * b, r.H - 2 * b, radius - b, hover ? t.SubtleHover : surface);
            p.FillCircle(r.X + radius, r.Y + radius, knob, t.ToggleOffKnob);
        }
    }

    // --- Standard button: light fill with a slightly darker bottom edge (Fluent "elevation" border).

    public static void Button(Painter p, UiFonts f, UiRect r, string text, bool hover, bool pressed, bool enabled)
    {
        var t = Theme.P;
        var b = f.Hairline;
        var radius = f.Px(4);
        var fill = !enabled ? t.ControlFillDisabled : pressed ? t.ControlFillPressed : hover ? t.ControlFillHover : t.ControlFill;

        p.FillRoundRect(r.X, r.Y, r.W, r.H, radius, enabled && !pressed ? t.ControlBorderBottom : t.ControlBorder);
        p.FillRoundRect(r.X, r.Y, r.W, r.H - b, radius, t.ControlBorder);
        p.FillRoundRect(r.X + b, r.Y + b, r.W - 2 * b, r.H - 2 * b, radius - b, fill);

        var color = !enabled ? t.TextDisabled : pressed ? t.TextSecondary : t.TextPrimary;
        p.Text(text, f.Body, color, r, TextCenter);
    }

    // --- Text input frame: thin border with a stronger bottom line that turns into a 2px accent
    // underline while focused.

    public static Rgb InputFill(bool focused) => focused ? Theme.P.InputFillFocused : Theme.P.ControlFill;

    public static void InputFrame(Painter p, UiFonts f, UiRect r, bool focused)
    {
        var t = Theme.P;
        var b = f.Hairline;
        var radius = f.Px(4);
        var underline = focused ? 2 * b : b;

        p.FillRoundRect(r.X, r.Y, r.W, r.H, radius, focused ? t.Accent : t.InputBorderBottom);
        p.FillRoundRect(r.X, r.Y, r.W, r.H - underline, radius, t.ControlBorder);
        p.FillRoundRect(r.X + b, r.Y + b, r.W - 2 * b, r.H - b - underline, radius - b, InputFill(focused));
    }
}
