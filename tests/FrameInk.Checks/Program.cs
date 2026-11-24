using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameInk;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CropCheck();
        CompositeCheck();
        Nv12Check();
        Console.WriteLine("PASS physical crop, PNG compositing, all ink tools, and NV12 conversion");
    }

    private static void CropCheck()
    {
        // A secondary monitor to the left of the primary, with a reserved taskbar and fractional DPI scale.
        var display = new Display(1, new(-1920, -80, 1920, 1080), new(-1920, -80, 1920, 1040), 120);
        Check(display.Fit(new(-1000, 100, 741, 503)) == new Crop(-1000, 100, 740, 502), "Even physical dimensions");
        var full = display.Fit(new(-9999, 10000, 9999, 9999));
        Check(full == new Crop(-1910, -70, 1900, 910), "Clamp rails and toolbar within negative-origin work area");
        Check(display.Fit(new(1, 999, 3, 5)) == new Crop(-736, 740, 726, 100), "Minimum crop and bottom-right clamp");
        Check(display.Chrome(display.Bounds, true) == new Crop(-1367, 825, 813, 120), "Centred full-screen toolbar above the work area edge");
        Throws<InvalidOperationException>(() => (display with { Work = new(-400, 0, 400, 250) }).Fit(full));
    }

    private static void CompositeCheck()
    {
        const int w = 128, h = 96;
        var rgb = new byte[w * h * 4];
        for (int i = 0; i < rgb.Length; i += 4) { rgb[i] = 195; rgb[i + 1] = 95; rgb[i + 2] = 60; }
        var baseImage = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, rgb, w * 4);
        var ink = new Ink();
        var clean = Bytes(Capture.Composite(baseImage, ink));
        var original = Color.FromRgb(60, 95, 195);
        Check(Pixel(clean, w, 0, 0) == original && Pixel(clean, w, w - 1, h - 1) == original, "Opaque base composite");
        ink.Begin(Tool.Pen, new(2, 10));
        ink.Move(new(40, 10));
        Check(Pixel(Bytes(Capture.Composite(baseImage, ink)), w, 28, 10) == Colors.OrangeRed, "In-progress pen included");
        ink.End();
        ink.Begin(Tool.Arrow, new(10, 32)); ink.Move(new(65, 32)); ink.End();
        ink.Begin(Tool.Rectangle, new(15, 45)); ink.Move(new(45, 75)); ink.End();
        var shapes = Bytes(Capture.Composite(baseImage, ink));
        Check(Pixel(shapes, w, 50, 37) == Colors.OrangeRed, "Arrowhead included");
        Check(Pixel(shapes, w, 15, 60) == Colors.OrangeRed && Pixel(shapes, w, 30, 60) == original, "Rectangle border only");
        ink.Begin(Tool.Step, new(80, 55));
        var first = Bytes(Capture.Composite(baseImage, ink));
        ink.Begin(Tool.Step, new(109, 76));
        var second = Bytes(Capture.Composite(baseImage, ink));
        Check(Pixel(second, w, 80, 62) == Colors.OrangeRed && Pixel(second, w, 109, 83) == Colors.OrangeRed, "Step badges included");
        ink.Undo();
        Check(Bytes(Capture.Composite(baseImage, ink)).SequenceEqual(first), "Undo removes only the last step");
        ink.Begin(Tool.Step, new(109, 76));
        Check(Bytes(Capture.Composite(baseImage, ink)).SequenceEqual(second), "Undo restores the step number");
        ink.Clear();
        Check(Bytes(Capture.Composite(baseImage, ink)).SequenceEqual(clean), "Clear returns exactly to the base image");
        ink.Begin(Tool.Pen, new(-4, 30)); ink.Move(new(4, 30));
        var clipped = Capture.Composite(baseImage, ink);
        Check(clipped.PixelWidth == w && clipped.PixelHeight == h && Pixel(Bytes(clipped), w, 0, 30) == Colors.OrangeRed, "Ink clips at the crop edge");

        ink.Clear();
        Check(InkPalette.Colours.Select(colour => colour.Fill.Color).Distinct().Count() == 6, "Six distinct ink swatches");
        var box = new Rect(16, 3, 82, 90);
        var blue = InkPalette.Colours[1];
        var draft = ink.BeginText(box, blue);
        draft.SetText("One two three four");
        var typing = Bytes(Capture.Composite(baseImage, ink));
        bool wrapped = false, selectedColour = false;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (Pixel(typing, w, x, y) == original) continue;
            Check(box.Contains(x, y), "Text ink stays inside its invisible bounds");
            if (y > 36) wrapped = true;
            if (Pixel(typing, w, x, y) == blue.Fill.Color) selectedColour = true;
        }
        Check(wrapped && Pixel(typing, w, 16, 3) == original, "Text wraps and does not render a box at its origin");
        Check(selectedColour, "Glyphs use the selected palette colour");
        ink.FinishText(keep: true);
        Check(Bytes(Capture.Composite(baseImage, ink)).SequenceEqual(typing), "Draft and committed text composite identically");
        ink.Undo();
        Check(Bytes(Capture.Composite(baseImage, ink)).SequenceEqual(clean), "Undo removes the entire text label");
        var tiny = ink.BeginText(new Rect(10, 10, 60, 34));
        tiny.SetText("one\ntwo\nthree");
        Check(tiny.Overflow, "Text clipped by box height reports overflow");
        ink.FinishText(keep: false);
        Check(Bytes(Capture.Composite(baseImage, ink)).SequenceEqual(clean), "Cancel drops the draft");
        ink.Begin(Tool.Pen, new(20, 10), blue); ink.Move(new(55, 10)); ink.End();
        ink.Begin(Tool.Rectangle, new(20, 25), InkPalette.Colours[3]); ink.Move(new(55, 50)); ink.End();
        var multicolour = Bytes(Capture.Composite(baseImage, ink));
        Check(Pixel(multicolour, w, 35, 10) == blue.Fill.Color && Pixel(multicolour, w, 35, 25) == InkPalette.Colours[3].Fill.Color,
            "New colours do not recolour earlier marks");
    }

    private static void Nv12Check()
    {
        byte[] input = [0,0,0,255, 255,255,255,255, 0,0,255,255, 255,0,0,255];
        var output = new byte[6];
        Nv12Pixels.Convert(input, output, 2, 2);
        Check(output.SequenceEqual(new byte[] { 16, 235, 82, 41, 146, 151 }), "Top-down luma and shared U/V");
        Throws<ArgumentException>(() => Nv12Pixels.Convert(input, output, 1, 4));
        var padded = new byte[16];
        new byte[] { 7, 8, 9, 255 }.CopyTo(padded, 0);
        Nv12Pixels.ExtendEdges(padded, 1, 1, 2, 2);
        Check(padded.SequenceEqual(Enumerable.Repeat(new byte[] { 7, 8, 9, 255 }, 4).SelectMany(x => x)),
            "Odd video edge duplicates the final real column and row");
    }

    private static byte[] Bytes(BitmapSource bitmap)
    {
        var buffer = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(buffer, bitmap.PixelWidth * 4, 0);
        return buffer;
    }

    private static Color Pixel(byte[] pixels, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}
