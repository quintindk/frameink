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

1. Drag the blue top rail to move the frame or the `◢` grip to resize it. **Full screen** selects the entire monitor the frame is on, including the taskbar. **Restore region** returns to the previous size and position. The toolbar shows the physical pixel dimensions and floats over the desktop in full-screen mode without appearing in captures.
2. **Interact** is click-through, even over visible ink. Click **Interact** to switch to **Draw**, or choose **Pen**, **Arrow**, **Rect** or **Step** to enter Draw directly. Use **Undo** or **Clear** to edit marks. Switching mode preserves them.
3. Use **Save PNG** or **Ctrl+Shift+F10**, in or out of a take. Use **Settings** to change the save folder or shortcut before recording. A hotkey collision does not disable the toolbar button or discard a working shortcut.
4. Press **Record** to start and **Stop** to finalise. The frame stays locked while recording and finalising. You can draw, switch modes and take PNGs without stopping the recording. **Close** during a take finalises it first and leaves the result visible. Press **Close** again to exit.

The default save folder is `%LOCALAPPDATA%\FrameInk\Captures`. Choose **Settings → Save location** to use another local fixed drive folder for both formats. The choice persists across restarts and does not move existing files. Network paths, linked directories and known OneDrive folders are rejected. The status line shows the save path and can be selected and copied. FrameInk does not upload, use an account, collect telemetry or record audio. Capture honours Windows protection and exclusion, so protected or excluded surfaces may be missing or blank.

## Limits and implementation

- A custom region and its toolbar must fit within one monitor's work area. Full screen uses that monitor's complete physical bounds, not the combined desktop. Custom regions are even-sized. On a monitor with an odd width or height, PNG keeps the exact dimensions and MP4 repeats the final column or row to satisfy H.264 without dropping desktop pixels. Marks keep their screen positions when switching between full screen and a custom region.
- The frame and ink are separate layered WPF windows. Both use [Windows capture exclusion](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity). The capture path refuses to run without it. The desktop crop comes from [GDI `CopyFromScreen`](https://learn.microsoft.com/dotnet/api/system.drawing.graphics.copyfromscreen), and WPF composites the current ink once. PNG and the [Media Foundation sink writer](https://learn.microsoft.com/windows/win32/medfound/sink-writer) take the same composited pixels.
- Recording targets up to 15 frames per second. Capture and encoding throughput can lower the actual rate. Video uses elapsed timestamps and a small queue rather than buffering an entire take. H.264 is lossy, so decoded pixels need not match PNG byte for byte.
- A display change invalidates the selected region. If recording, FrameInk stops capture and tries to finalise the valid portion. Drag the top rail to reselect a custom region, or choose **Restore region** and then **Full screen** to use the whole monitor again.

See the [MVP specification](docs/spec.md) and [Windows tests and results](docs/windows-smoke.md).
