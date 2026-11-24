using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FrameInk;

internal enum Tool { Pen, Arrow, Rectangle, Step }

internal sealed class Ink
{
    private sealed record Mark(Tool Tool, List<Point> Points, int Step);
    private readonly List<Mark> marks = new();
    private Mark? current;
    private int steps;
    private static readonly Brush Colour = Brushes.OrangeRed;
    private static readonly Pen Line = new(Colour, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    public void Begin(Tool tool, Point point)
    {
        End();
        current = new(tool, new() { point }, tool == Tool.Step ? ++steps : 0);
        if (tool == Tool.Step) End();
    }

    public void Move(Point point)
    {
        if (current is not { } m || (point - m.Points[^1]).Length < 1) return;
        if (m.Tool == Tool.Pen || m.Points.Count == 1) m.Points.Add(point);
        else m.Points[1] = point;
    }

    public void End()
    {
        if (current is { } mark) marks.Add(mark);
        current = null;
    }

    public void Undo()
    {
        End();
        if (marks.Count == 0) return;
        if (marks[^1].Tool == Tool.Step) steps--;
        marks.RemoveAt(marks.Count - 1);
    }

    public void Clear() { current = null; marks.Clear(); steps = 0; }

    public void Render(DrawingContext context, int width, int height)
    {
        context.PushClip(new RectangleGeometry(new(0, 0, width, height)));
        foreach (var mark in marks) Draw(context, mark);
        if (current is { } m) Draw(context, m);
        context.Pop();
    }

    private static void Draw(DrawingContext context, Mark mark)
    {
        var start = mark.Points[0];
        var end = mark.Points[^1];
        switch (mark.Tool)
        {
            case Tool.Pen:
                if (mark.Points.Count == 1) { context.DrawEllipse(Colour, null, start, 2, 2); break; }
                var path = new StreamGeometry();
                using (var g = path.Open())
                {
                    g.BeginFigure(start, false, false);
                    g.PolyLineTo(mark.Points.GetRange(1, mark.Points.Count - 1), true, true);
                }
                context.DrawGeometry(null, Line, path);
                break;
            case Tool.Arrow:
                Vector v = end - start;
                if (v.Length < 2) break;
                v.Normalize();
                var stem = end - 15 * v;
                var side = new Vector(-v.Y, v.X) * 7;
                context.DrawLine(Line, start, end);
                context.DrawLine(Line, end, stem + side);
                context.DrawLine(Line, end, stem - side);
                break;
            case Tool.Rectangle:
                if (start != end) context.DrawRectangle(null, Line, new(start, end));
                break;
            case Tool.Step:
                var text = new FormattedText(mark.Step.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 15, Brushes.White, 1);
                context.DrawEllipse(Colour, null, start, Math.Max(12, text.Width / 2 + 4), 12);
                context.DrawText(text, new(start.X - text.Width / 2, start.Y - text.Height / 2));
                break;
        }
    }
}

internal sealed class InkSurface(Ink ink) : FrameworkElement
{
    public Crop Crop { get; set; }
    protected override void OnRender(DrawingContext context)
    {
        // A nonzero alpha gives Draw mode an HWND hit surface even where there is no ink.
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, new(RenderSize));
        if (!RenderSize.IsEmpty && Crop.Width > 0 && Crop.Height > 0)
        {
            context.PushTransform(new ScaleTransform(RenderSize.Width / Crop.Width, RenderSize.Height / Crop.Height));
            ink.Render(context, Crop.Width, Crop.Height);
            context.Pop();
        }
    }
}
