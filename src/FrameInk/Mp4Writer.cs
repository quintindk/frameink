using System;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace FrameInk;

// The small Media Foundation ABI used here is defined by mfapi.h, mfobjects.h and mfreadwrite.h.
// All COM pointers are created, used and released on the take's encoder thread.
internal sealed unsafe class Mp4Writer : IDisposable
{
    private static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"),
        Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),
        FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d"),
        FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"),
        PixelAspect = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"),
        Interlace = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd"),
        AverageBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e"),
        YuvMatrix = new("3e23d450-2c75-4d25-a00e-b91670d12327"),
        Video = FourCc(0x73646976), H264 = FourCc(0x34363248), Nv12 = FourCc(0x3231564e);

    [DllImport("ole32")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32")] private static extern void CoUninitialize();
    [DllImport("mfplat")] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat")] private static extern int MFShutdown();
    [DllImport("mfplat")] private static extern int MFCreateMediaType(out nint mediaType);
    [DllImport("mfplat")] private static extern int MFCreateSample(out nint sample);
    [DllImport("mfplat")] private static extern int MFCreateMemoryBuffer(int size, out nint buffer);
    [DllImport("mfreadwrite", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSinkWriterFromURL(string url, nint byteStream, nint attributes, out nint writer);

    private nint writer;
    private uint stream;
    private bool com, mediaFoundation;
    private readonly int width, height;
    private readonly byte[] pixels;

    public Mp4Writer(string path, int width, int height)
    {
        this.width = width;
        this.height = height;
        pixels = new byte[checked(width * height * 4)];
        try
        {
            Check(CoInitializeEx(0, 0), "Initialise COM");
            com = true;
            Check(MFStartup(0x00020070, 0), "Start Windows Media Foundation");
            mediaFoundation = true;
            Check(MFCreateSinkWriterFromURL(path, 0, 0, out writer), "Create MP4 sink");

            nint output = Type(H264);
            try
            {
                SetUInt(output, AverageBitrate, (uint)Math.Clamp((long)width * height * 5, 1_500_000, 35_000_000));
                uint index;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)Slot(writer, 3))(writer, output, &index), "Add H.264 stream");
                stream = index;
            }
            finally { Release(output); }

            nint input = Type(Nv12);
            try
            {
                Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Slot(writer, 4))(writer, stream, input, 0), "Set NV12 encoder input");
            }
            finally { Release(input); }
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(writer, 5))(writer), "Begin encoding");
        }
        catch { Dispose(); throw; }
    }

    public void Write(BitmapSource frame, long time, long duration)
    {
        if (frame.PixelWidth != width || frame.PixelHeight != height) throw new InvalidOperationException("Recording frame dimensions changed.");
        frame.CopyPixels(pixels, width * 4, 0);
        int bytes = checked(width * height * 3 / 2);
        Check(MFCreateMemoryBuffer(bytes, out nint buffer), "Allocate video sample buffer");
        try
        {
            byte* data;
            Check(((delegate* unmanaged[Stdcall]<nint, byte**, int*, int*, int>)Slot(buffer, 3))(buffer, &data, null, null), "Lock video buffer");
            try { Nv12Pixels.Convert(pixels, new Span<byte>(data, bytes), width, height); }
            finally { Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(buffer, 4))(buffer), "Unlock video buffer"); }
            Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(buffer, 6))(buffer, bytes), "Set video buffer length");

            Check(MFCreateSample(out nint sample), "Create video sample");
            try
            {
                Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(sample, 36))(sample, time), "Set video timestamp");
                Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(sample, 38))(sample, duration), "Set video duration");
                Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(sample, 42))(sample, buffer), "Attach video buffer");
                Check(((delegate* unmanaged[Stdcall]<nint, uint, nint, int>)Slot(writer, 6))(writer, stream, sample), "Write video sample");
            }
            finally { Release(sample); }
        }
        finally { Release(buffer); }
    }

    public void Finish() => Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(writer, 11))(writer), "Finalise MP4");

    private nint Type(Guid sub)
    {
        Check(MFCreateMediaType(out nint type), "Create video media type");
        try
        {
            SetGuid(type, MajorType, Video);
            SetGuid(type, Subtype, sub);
            SetUInt(type, Interlace, 2); // progressive
            SetUInt(type, YuvMatrix, 2); // BT.601
            SetPair(type, FrameSize, (uint)width, (uint)height);
            SetPair(type, FrameRate, Take.Fps, 1);
            SetPair(type, PixelAspect, 1, 1);
            return type;
        }
        catch { Release(type); throw; }
    }

    private static Guid FourCc(int code) => new(code, 0, 0x0010, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71);
    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];
    private static void SetGuid(nint type, Guid key, Guid value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slot(type, 24))(type, &key, &value), $"Set media type GUID {key}");
    private static void SetUInt(nint type, Guid key, uint value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)Slot(type, 21))(type, &key, value), $"Set media type attribute {key}");
    private static void SetPair(nint type, Guid key, uint first, uint second) =>
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, ulong, int>)Slot(type, 22))(type, &key, ((ulong)first << 32) | second), $"Set media type pair {key}");
    private static void Check(int hr, string operation)
    {
        if (hr < 0) throw new COMException($"{operation} (0x{hr:X8}).", hr);
    }

    private static void Release(nint instance) { if (instance != 0) Marshal.Release(instance); }

    public void Dispose()
    {
        Release(writer);
        writer = 0;
        if (mediaFoundation) { MFShutdown(); mediaFoundation = false; }
        if (com) { CoUninitialize(); com = false; }
    }
}
