using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FrameInk;

internal sealed partial class FrameWindow
{
    private Button recordButton = null!;
    private Take? take;
    private DispatcherTimer? timer;
    private bool finishing;
    private bool Locked => take is not null;
    private string RecordingCaption => Locked ? (finishing ? "  |  FINALISING" : "  |  ● REC") : "";

    private void AddRecordButton(Panel panel) => recordButton = Button(panel, "Record", RecordOrStop, "RecordingButton");

    private void RecordOrStop()
    {
        if (finishing || configuring || closing) return;
        if (take is { } existing) { _ = Stop(existing); return; }
        try
        {
            ValidateCrop();
            string path = Capture.OutputPath(".mp4", saveFolder);
            var first = Capture.Grab(crop, ink);
            var active = take = new Take(first, path);
            SetRecordingUI();
            ShowStatus("Recording video only. The frame is locked; ink and PNG are available.");
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1000d / Take.Fps) };
            timer.Tick += (_, _) => RecordFrame(active);
            timer.Start();
            _ = WatchEncoder(active);
        }
        catch (Exception ex) { ShowStatus($"Cannot start recording: {ex.Message}"); }
    }

    private void RecordFrame(Take active)
    {
        if (take != active || finishing) return;
        try
        {
            ValidateCrop();
            active.Add(Capture.Grab(crop, ink));
        }
        catch (Exception ex) { _ = Stop(active, ex.Message); }
    }

    private void IncludeSnapshot(BitmapSource frame)
    {
        if (!finishing) take?.Add(frame);
    }

    private async Task WatchEncoder(Take active)
    {
        try { await active.Completion; }
        catch (Exception ex)
        {
            if (take != active || finishing) return;
            timer?.Stop();
            timer = null;
            take = null;
            SetRecordingUI();
            ShowStatus($"MP4 failed: {ex.Message}");
        }
    }

    private async Task Stop(Take active, string? reason = null)
    {
        if (finishing || take != active) return;
        finishing = true;
        timer?.Stop();
        timer = null;
        SetRecordingUI();
        ShowStatus(reason is null ? "Finalising MP4…" : $"Recording interrupted: {reason} Finalising the valid portion…");
        active.Stop();
        try
        {
            await active.Completion;
            ShowStatus(reason is null ? $"MP4 saved: {active.Path}" : $"Recording interrupted: {reason} Partial take saved: {active.Path}");
        }
        catch (Exception ex) { ShowStatus($"MP4 failed: {ex.Message}"); }
        finally
        {
            take = null;
            finishing = false;
            SetRecordingUI();
            if (closing)
            {
                // Leave the result visible, including the path or encoder error, before allowing an explicit Close.
                closing = false;
            }
        }
    }

    private void SetRecordingUI()
    {
        recordButton.IsEnabled = !finishing;
        recordButton.Content = take is null ? "Record" : "Stop";
        settings.IsEnabled = !Locked;
        screen.IsEnabled = !Locked;
        move.Cursor = Locked ? Cursors.Arrow : Cursors.SizeAll;
        grip.Cursor = Locked ? Cursors.Arrow : Cursors.SizeNWSE;
        UpdateSize();
    }

    private void DisplayInvalidated()
    {
        if (!invalid) return;
        string message = $"Display changed. {ReselectHint}";
        if (take is { } active) { _ = Stop(active, message); }
        else ShowStatus(message);
    }

    private void PrepareClose(CancelEventArgs e)
    {
        if (savingPng)
        {
            e.Cancel = true;
            ShowStatus("Saving PNG. Close again once the save finishes.");
            return;
        }
        closing = true;
        if (take is { } active)
        {
            e.Cancel = true;
            _ = Stop(active);
        }
        else if (hotkeyId != 0) Native.UnregisterHotKey(Native.Handle(this), hotkeyId);
    }
}
