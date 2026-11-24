# FrameInk

FrameInk is a native Windows recorder with a persistent, movable capture frame and live annotations. It saves local PNGs and silent H.264 MP4s. It does not capture until you press **Save PNG**, use the global PNG shortcut, or press **Record**.

## Build and run

Use the **Windows** .NET 10 SDK and an interactive Windows desktop. The capture exclusion API requires Windows 10 version 2004 or later. This build was tested on Windows 11. Recording needs Windows Media Foundation and its H.264 encoder. Windows N editions may need the Media Feature Pack. No third-party runtime tools are required.

```powershell
dotnet build .\src\FrameInk\FrameInk.csproj -c Release
.\src\FrameInk\bin\Release\net10.0-windows\FrameInk.exe
```

Build and run from a Windows path. WSL can be used to edit the repository, but a Linux-side build does not test Windows desktop behaviour.

## Use it

1. Drag the blue top rail to move the frame. Drag the `◢` grip to resize. The toolbar sits below the capture area and shows the physical pixel dimensions.
2. **Interact** is click-through, even over visible ink. Click **Interact** to switch to **Draw**, or choose **Pen**, **Arrow**, **Rect** or **Step** to enter Draw directly. Use **Undo** or **Clear** to edit marks. Switching mode preserves them.
3. Use **Save PNG** or **Ctrl+Shift+F10**, in or out of a take. Use **Hotkey** to change the shortcut before recording. A collision does not disable the toolbar button or discard a working shortcut.
4. Press **Record** to start and **Stop** to finalise. The frame stays locked while recording and finalising. You can draw, switch modes and take PNGs without stopping the recording. **Close** during a take finalises it first and leaves the result visible. Press **Close** again to exit.

Completed files go to `%LOCALAPPDATA%\FrameInk\Captures`. The status line shows the save path and can be selected and copied. There is no audio, upload, telemetry or account. Capture honours Windows protection and exclusion, so protected or excluded surfaces may be missing or blank.

## Limits and implementation

- A region and its toolbar must fit within one monitor's work area. The capture dimensions are even so PNG and H.264 use exactly the same crop, without padding. Marks use region-relative physical pixels and keep their offsets on move or resize.
- The frame and ink are separate layered WPF windows. Both use [Windows capture exclusion](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity). The capture path refuses to run without it. The desktop crop comes from [GDI `CopyFromScreen`](https://learn.microsoft.com/dotnet/api/system.drawing.graphics.copyfromscreen), and WPF composites the current ink once. PNG and the [Media Foundation sink writer](https://learn.microsoft.com/windows/win32/medfound/sink-writer) take the same composited pixels.
- Recording targets up to 15 frames per second. Capture and encoding throughput can lower the actual rate. Video uses elapsed timestamps and a small queue rather than buffering an entire take. H.264 is lossy, so decoded pixels need not match PNG byte for byte.
- A display change invalidates the selected region. If recording, FrameInk stops capture and tries to finalise the valid portion. Drag the top rail to reselect the region before capturing again.

See the [MVP specification](docs/spec.md) and [Windows tests and results](docs/windows-smoke.md).
