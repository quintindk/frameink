using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FrameInk;

internal readonly record struct Crop(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

internal readonly record struct Display(nint Handle, Crop Bounds, Crop Work, uint Dpi)
{
    private const int MinimumToolbarDip = 580;
    public int Pixels(double dip) => (int)Math.Ceiling(dip * Dpi / 96d);
    public int Border => Pixels(8);
    public int Bar => Pixels(96);

    public Crop Chrome(Crop crop, bool fullScreen)
    {
        if (!fullScreen) return new(crop.X - Border, crop.Y - Border, crop.Width + 2 * Border, crop.Height + Border + Bar);
        int width = Math.Min(Work.Width, Pixels(650));
        if (width < Pixels(MinimumToolbarDip) || Work.Height < Bar) throw new InvalidOperationException("This monitor is too small for the toolbar.");
        return new(Work.X + (Work.Width - width) / 2, Work.Bottom - Bar - Math.Min(Pixels(12), Work.Height - Bar), width, Bar);
    }

    public Crop Fit(Crop request)
    {
        int border = Border;
        int maxW = (Work.Width - 2 * border) & ~1;
        int maxH = (Work.Height - border - Bar) & ~1;
        int minW = (Pixels(MinimumToolbarDip) + 1) & ~1;
        int minH = (Pixels(80) + 1) & ~1;
        if (maxW < minW || maxH < minH)
            throw new InvalidOperationException("This monitor's work area is too small for the frame and toolbar.");
        int w = Math.Clamp(request.Width & ~1, minW, maxW);
        int h = Math.Clamp(request.Height & ~1, minH, maxH);
        return new(Math.Clamp(request.X, Work.X + border, Work.Right - border - w),
                   Math.Clamp(request.Y, Work.Y + border, Work.Bottom - Bar - h), w, h);
    }
}

internal static class Native
{
    public const int HotkeyMessage = 0x312, DisplayChange = 0x7e, DpiChange = 0x02e0;
    private const int ExtendedStyle = -20, Transparent = 0x20, Layered = 0x80000;
    private const uint ExcludeAffinity = 0x11, NoZOrderOrActivate = 0x14;
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly Crop Crop => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public int Size;
        public Rect Bounds, Work;
        public int Flags;
    }

    [DllImport("user32", SetLastError = true)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32", SetLastError = true)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32", SetLastError = true, EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(nint window, int index);
    [DllImport("user32", SetLastError = true, EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32", SetLastError = true)] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32", SetLastError = true)] public static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32", SetLastError = true)] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32")] private static extern bool IsIconic(nint window);
    [DllImport("user32")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32", SetLastError = true)] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32", SetLastError = true)] private static extern bool ClientToScreen(nint window, ref Point point);

    public static Point Cursor() => GetCursorPos(out var p) ? p : throw new Win32Exception();
    public static bool TryCursor(out Point point) => GetCursorPos(out point);

    public static Display MonitorAt(int x, int y)
    {
        nint handle = MonitorFromPoint(new() { X = x, Y = y }, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(handle, ref info)) throw new Win32Exception();
        Marshal.ThrowExceptionForHR(GetDpiForMonitor(handle, 0, out uint dpi, out _));
        return new(handle, info.Bounds.Crop, info.Work.Crop, dpi);
    }

    public static nint Handle(Window window) => new WindowInteropHelper(window).Handle;

    public static void ExcludeFromCapture(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("Windows 10 version 2004 or later is required for capture exclusion.");
        if (!SetWindowDisplayAffinity(Handle(window), ExcludeAffinity)) throw new Win32Exception(); // WDA_EXCLUDEFROMCAPTURE
    }

    public static bool IsCaptureExcludedAndVisible(Window window)
    {
        nint hwnd = Handle(window);
        return IsWindowVisible(hwnd) && !IsIconic(hwnd) && GetWindowDisplayAffinity(hwnd, out uint affinity) && affinity == ExcludeAffinity;
    }

    public static void Place(Window window, Crop bounds)
    {
        // Keep z order and activation unchanged. All inputs are physical pixels.
        if (!SetWindowPos(Handle(window), 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, NoZOrderOrActivate))
            throw new Win32Exception();
    }

    public static Crop Client(nint window)
    {
        var origin = new Point();
        if (!GetClientRect(window, out var rect) || !ClientToScreen(window, ref origin)) throw new Win32Exception();
        return new(origin.X, origin.Y, rect.Right, rect.Bottom);
    }

    public static void SetClickThrough(Window window, bool enabled)
    {
        nint hwnd = Handle(window);
        int style = GetWindowLong(hwnd, ExtendedStyle);
        if (style == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception();
        if ((style & Layered) == 0) throw new InvalidOperationException("The drawing overlay is not layered.");
        if (SetWindowLong(hwnd, ExtendedStyle, enabled ? style | Transparent : style & ~Transparent) == 0
            && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception();
    }
}
