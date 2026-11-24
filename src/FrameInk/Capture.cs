using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Gdi = System.Drawing;
using GdiImage = System.Drawing.Imaging;

namespace FrameInk;

internal static class Capture
{
    public static BitmapSource Grab(Crop crop, Ink ink)
    {
        using var bitmap = new Gdi.Bitmap(crop.Width, crop.Height, GdiImage.PixelFormat.Format32bppRgb);
        using (var screen = Gdi.Graphics.FromImage(bitmap))
        {
            // The two recorder HWNDs use WDA_EXCLUDEFROMCAPTURE. Omitting CAPTUREBLT alone is not sufficient.
            screen.CopyFromScreen(crop.X, crop.Y, 0, 0, bitmap.Size, Gdi.CopyPixelOperation.SourceCopy);
        }
        var bits = bitmap.LockBits(new(0, 0, crop.Width, crop.Height), GdiImage.ImageLockMode.ReadOnly, bitmap.PixelFormat);
        try
        {
            var desktop = BitmapSource.Create(crop.Width, crop.Height, 96, 96, PixelFormats.Bgr32, null,
                bits.Scan0, checked(bits.Stride * crop.Height), bits.Stride);
            return Composite(desktop, ink);
        }
        finally { bitmap.UnlockBits(bits); }
    }

    internal static BitmapSource Composite(BitmapSource desktop, Ink ink)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(desktop, new Rect(0, 0, desktop.PixelWidth, desktop.PixelHeight));
            ink.Render(dc, desktop.PixelWidth, desktop.PixelHeight);
        }
        var output = new RenderTargetBitmap(desktop.PixelWidth, desktop.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        output.Render(visual);
        output.Freeze();
        return output;
    }

    public static void SavePng(BitmapSource frame, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        string staging = path + ".partial";
        bool created = false;
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew))
            {
                created = true;
                encoder.Save(stream);
            }
            File.Move(staging, path);
        }
        catch { if (created) RemoveStagingFile(staging); throw; }
    }

    public static string OutputPath(string extension, string folder) => Path.Combine(CaptureFolder.Ensure(folder),
        $"FrameInk-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}" + extension);

    internal static void RemoveStagingFile(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
