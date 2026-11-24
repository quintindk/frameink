using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FrameInk;

internal sealed class TextAnnotation(Rect bounds, InkColour colour)
{
    public const double FontPixels = 24, MinimumWidth = 40, MinimumHeight = 34;
    public const double Inset = 2;
    public const int CharacterLimit = 2000;
    public static readonly FontFamily Family = new("Segoe UI");
    private static readonly Typeface Face = new(Family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Pen DarkOutline = new(Brushes.Black, 2) { LineJoin = PenLineJoin.Round };
    private static readonly Pen LightOutline = new(Brushes.White, 2) { LineJoin = PenLineJoin.Round };
    private Geometry? glyphs;
    public Rect Bounds { get; private set; } = bounds;
    public InkColour Colour { get; } = colour;
    public Color OutlineColour => Colour.Fill.Color == Colors.Black ? Colors.White : Colors.Black;
    public bool HasText => glyphs is not null;
    public bool Overflow { get; private set; }

    public void SetText(string value)
    {
        glyphs = null;
        Overflow = false;
        if (string.IsNullOrWhiteSpace(value)) return;
        var layout = new FormattedText(value.TrimEnd(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Face, FontPixels, Colour.Fill, 1)
        {
            MaxTextWidth = Bounds.Width - 2 * Inset,
            Trimming = TextTrimming.None
        };
        double height = Bounds.Height - 2 * Inset;
        Overflow = layout.Height > height;
        layout.MaxTextHeight = height;
        glyphs = layout.BuildGeometry(new(Inset, Inset));
        glyphs.Freeze();
    }

    public void Translate(Vector offset)
    {
        var box = Bounds;
        box.Offset(offset);
        Bounds = box;
    }

    public void Render(DrawingContext context)
    {
        if (glyphs is null) return;
        context.PushClip(new RectangleGeometry(Bounds));
        context.PushTransform(new TranslateTransform(Bounds.X, Bounds.Y));
        context.DrawGeometry(null, OutlineColour == Colors.Black ? DarkOutline : LightOutline, glyphs);
        context.DrawGeometry(Colour.Fill, null, glyphs);
        context.Pop();
        context.Pop();
    }
}
