using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FrameInk;

internal enum Tool { Pen, Arrow, Rectangle, Step, Text }

internal sealed class Ink
{
    private sealed record Mark(Tool Tool, List<Point> Points, int Step, InkColour Colour, TextAnnotation? Text = null);
    private readonly List<Mark> marks = new();
    private Mark? current;
    private TextAnnotation? draft;
    private int steps;

    public void Begin(Tool tool, Point point, InkColour? colour = null)
    {
        End();
        current = new(tool, new() { point }, tool == Tool.Step ? ++steps : 0, colour ?? InkPalette.Default);
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

    public void Clear() { current = null; draft = null; marks.Clear(); steps = 0; }

    public TextAnnotation BeginText(Rect bounds, InkColour? colour = null) => draft = new(bounds, colour ?? InkPalette.Default);

    public void FinishText(bool keep)
    {
        if (keep && draft is { HasText: true } label) marks.Add(new(Tool.Text, [], 0, label.Colour, label));
        draft = null;
    }

    public void Translate(int x, int y)
    {
        End();
        var offset = new Vector(x, y);
        foreach (var mark in marks)
        {
            mark.Text?.Translate(offset);
            for (int i = 0; i < mark.Points.Count; i++) mark.Points[i] += offset;
        }
    }

    public void Render(DrawingContext context, int width, int height, bool includeDraft = true)
    {
        context.PushClip(new RectangleGeometry(new(0, 0, width, height)));
        foreach (var mark in marks) Draw(context, mark);
        if (current is { } m) Draw(context, m);
        if (includeDraft) draft?.Render(context);
        context.Pop();
    }

    private static void Draw(DrawingContext context, Mark mark)
    {
        if (mark.Text is { } textLabel) { textLabel.Render(context); return; }
        var start = mark.Points[0];
        var end = mark.Points[^1];
        var fill = mark.Colour.Fill;
        var line = mark.Colour.Stroke;
        switch (mark.Tool)
        {
            case Tool.Pen:
                if (mark.Points.Count == 1) { context.DrawEllipse(fill, null, start, 2, 2); break; }
                var path = new StreamGeometry();
                using (var g = path.Open())
                {
                    g.BeginFigure(start, false, false);
                    g.PolyLineTo(mark.Points.GetRange(1, mark.Points.Count - 1), true, true);
                }
                context.DrawGeometry(null, line, path);
                break;
            case Tool.Arrow:
                Vector v = end - start;
                if (v.Length < 2) break;
                v.Normalize();
                var stem = end - 15 * v;
                var side = new Vector(-v.Y, v.X) * 7;
                context.DrawLine(line, start, end);
                context.DrawLine(line, end, stem + side);
                context.DrawLine(line, end, stem - side);
                break;
            case Tool.Rectangle:
                if (start != end) context.DrawRectangle(null, line, new(start, end));
                break;
            case Tool.Step:
                var text = new FormattedText(mark.Step.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 15, mark.Colour.Contrast, 1);
                context.DrawEllipse(fill, null, start, Math.Max(12, text.Width / 2 + 4), 12);
                context.DrawText(text, new(start.X - text.Width / 2, start.Y - text.Height / 2));
                break;
        }
    }
}

internal sealed class InkSurface(Ink ink) : FrameworkElement
{
    public Crop Crop { get; set; }
    public Crop? Toolbar { get; set; }
    protected override void OnRender(DrawingContext context)
    {
        if (!RenderSize.IsEmpty && Crop.Width > 0 && Crop.Height > 0)
        {
            context.PushTransform(new ScaleTransform(RenderSize.Width / Crop.Width, RenderSize.Height / Crop.Height));
            var bounds = new Rect(0, 0, Crop.Width, Crop.Height);
            if (Toolbar is { } bar)
            {
                // In Draw mode the owned ink HWND is above the toolbar. An alpha-zero hole lets the real controls receive clicks.
                context.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds),
                    new RectangleGeometry(new(bar.X - Crop.X, bar.Y - Crop.Y, bar.Width, bar.Height))));
            }
            // Nonzero alpha gives Draw an HWND hit surface where there is no ink.
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, bounds);
            // The real text editor displays the draft live. Capture renders it separately without the caret or selection.
            ink.Render(context, Crop.Width, Crop.Height, includeDraft: false);
            if (Toolbar is not null) context.Pop();
            context.Pop();
        }
    }
}
