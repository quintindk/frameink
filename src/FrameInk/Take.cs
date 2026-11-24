using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace FrameInk;

internal sealed class Take
{
    public const int Fps = 15;
    public string Path { get; }
    public Task Completion { get; }
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly record struct Frame(BitmapSource Image, long Time);
    private readonly Channel<Frame> frames = Channel.CreateBounded<Frame>(new BoundedChannelOptions(3)
    {
        SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest
    });
    private long lastTime, endTime;

    public Take(BitmapSource first, string path)
    {
        Path = path;
        Completion = Task.Factory.StartNew(() => Encode(new(first, 0)), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Add(BitmapSource image)
    {
        long now = lastTime = Math.Max(lastTime + 1, clock.Elapsed.Ticks);
        frames.Writer.TryWrite(new(image, now));
    }

    public void Stop()
    {
        endTime = Math.Max(lastTime + 1, clock.Elapsed.Ticks);
        frames.Writer.TryComplete();
    }

    private void Encode(Frame first)
    {
        // Media Foundation chooses the container based on the extension. The public name is only exposed after finalisation.
        string temp = System.IO.Path.ChangeExtension(Path, ".partial.mp4");
        try
        {
            using (var encoder = new Mp4Writer(temp, first.Image.PixelWidth, first.Image.PixelHeight))
            {
                Frame pending = first;
                while (frames.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    while (frames.Reader.TryRead(out var next))
                    {
                        encoder.Write(pending.Image, pending.Time, next.Time - pending.Time);
                        pending = next;
                    }
                }
                encoder.Write(pending.Image, pending.Time, Math.Max(endTime - pending.Time, TimeSpan.TicksPerSecond / Fps));
                encoder.Finish();
            }
            File.Move(temp, Path);
        }
        catch
        {
            Capture.RemoveStagingFile(temp);
            throw;
        }
    }
}
