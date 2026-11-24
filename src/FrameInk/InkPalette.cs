using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace FrameInk;

internal sealed class InkColour
{
    public string Name { get; }
    public SolidColorBrush Fill { get; }
    public Brush Contrast { get; }
    public Pen Stroke { get; }

    public InkColour(string name, Color colour)
    {
        Name = name;
        Fill = new(colour);
        Fill.Freeze();
        Contrast = colour.R * .299 + colour.G * .587 + colour.B * .114 >= 150 ? Brushes.Black : Brushes.White;
        Stroke = new(Fill, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        Stroke.Freeze();
    }
}

internal static class InkPalette
{
    public static IReadOnlyList<InkColour> Colours { get; } = Array.AsReadOnly(new InkColour[]
    {
        new("Orange", Colors.OrangeRed),
        new("Blue", Color.FromRgb(75, 199, 255)),
        new("Green", Color.FromRgb(123, 230, 95)),
        new("Yellow", Color.FromRgb(255, 213, 77)),
        new("White", Colors.White),
        new("Black", Colors.Black)
    });

    public static InkColour Default => Colours[0];
}
