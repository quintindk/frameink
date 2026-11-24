using System;

namespace FrameInk;

internal static class Nv12Pixels
{
    // Top-down, opaque BGRA to limited-range BT.601 NV12. Luma is per pixel; each 2x2 tile shares averaged chroma.
    public static void Convert(ReadOnlySpan<byte> bgra, Span<byte> nv12, int width, int height)
    {
        int area = checked(width * height);
        if (width <= 0 || height <= 0 || ((width | height) & 1) != 0 || bgra.Length != checked(area * 4) || nv12.Length != area + area / 2)
            throw new ArgumentException("NV12 requires even frame dimensions and tightly packed planes.");

        for (int y = 0; y < height; y += 2)
        {
            int top = y * width, bottom = top + width, uv = area + top / 2;
            for (int x = 0; x < width; x += 2)
            {
                int r = 0, g = 0, b = 0;
                nv12[top + x] = Luma(bgra, top + x, ref r, ref g, ref b);
                nv12[top + x + 1] = Luma(bgra, top + x + 1, ref r, ref g, ref b);
                nv12[bottom + x] = Luma(bgra, bottom + x, ref r, ref g, ref b);
                nv12[bottom + x + 1] = Luma(bgra, bottom + x + 1, ref r, ref g, ref b);
                nv12[uv + x] = (byte)(128 + ((-38 * r - 74 * g + 112 * b + 512) >> 10));
                nv12[uv + x + 1] = (byte)(128 + ((112 * r - 94 * g - 18 * b + 512) >> 10));
            }
        }
    }

    private static byte Luma(ReadOnlySpan<byte> bgra, int pixel, ref int sumR, ref int sumG, ref int sumB)
    {
        int i = pixel * 4;
        int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
        sumB += b; sumG += g; sumR += r;
        return (byte)(16 + ((66 * r + 129 * g + 25 * b + 128) >> 8));
    }
}
