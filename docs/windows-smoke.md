# Windows validation

Tested on 24 November 2026 in an interactive Windows 11 desktop session, OS build `26300`, with Windows .NET SDK `10.0.404`. The session exposed one monitor at 120 DPI (125%). All captures used a local, opaque synthetic pattern window. No customer desktop content or media is checked into the repository.

## Run the checks

From a Windows PowerShell prompt in the repository:

```powershell
dotnet build .\tests\FrameInk.Checks\FrameInk.Checks.csproj -c Release
.\tests\FrameInk.Checks\bin\Release\net10.0-windows\FrameInk.Checks.exe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\windows-smoke.ps1 `
    -App .\src\FrameInk\bin\Release\net10.0-windows\FrameInk.exe
```

Close other FrameInk instances. The smoke test moves the pointer and sends mouse and keyboard input to its synthetic window and FrameInk. Leave the desktop unlocked and do not interact with it while the test runs. The smoke test expects the default hotkey. `-MonitorIndex` selects an attached monitor by zero-based Windows enumeration index. `-PngOnly` skips video. Add `-TestHotkeyRebind` to exercise changing and restoring the shortcut when no `hotkey.json` exists. The test removes the setting it created.

To check the video independently, add `-KeepArtifacts` to the full run. It prints `VIDEO`, `PLAIN` and `MID` paths. Pass those paths to the following command with Python 3.8+, FFmpeg and FFprobe installed. WSL paths can be used for this offline check. These tools are not app dependencies.

```text
python tests/check-video.py <VIDEO> <PLAIN> <MID>
```

## Actual outcomes

| Check | Outcome |
|---|---|
| Windows build and automated component checks | Passed. Zero build warnings and errors. Checked crop clamping and even dimensions, including a synthetic negative-origin monitor. Checked per-pixel composition, clipped ink, undo, step numbering, clear and top-down NV12 conversion. |
| Move, resize and attached toolbar | Passed. Rail moved the crop without resizing it. Grip resized from the fixed upper-left corner. Plain PNG was exactly **790 × 456** and every pixel matched the known desktop crop, including all edges. |
| Interact and Draw | Passed. Physical clicks reached the synthetic window in Interact, including over visible ink. Draw did not pass clicks through. Pen, arrow, rectangle and numbered steps were exercised through the toolbar. Undo removed the last stroke. |
| Overlay exclusion and PNG parity | Passed. Raw GDI under the visible ink matched the synthetic base pixel. The app PNG contained the ink and no chrome. Toolbar and global-hotkey PNGs were byte-identical for the same state. |
| Global hotkey configuration | Passed. A Win32 reservation caused a visible collision error without disabling toolbar PNG. Rebinding to Ctrl+Shift+F9 released the old binding, captured the same PNG and survived restart. |
| Recording and mid-take PNG | Passed. Drag and resize had no effect during recording. Drawing and click-through mode switches still worked. A hotkey PNG matched the stand-alone annotated PNG, and recording continued. Stop finalised the MP4 and unlocked the frame. |
| MP4 playback and visual match | Passed. Windows `MediaPlayer` reached `MediaEnded`. FFmpeg decoded all 53 frames of the 5.20 s sample. H.264 video only, 790 × 456, with advancing timestamps. The decoded sequence showed clean → ink matching the PNG in 20 frames → clean, with no chrome at the crop edges. |
| Invalidated crop recovery | Passed with a simulated `WM_DISPLAYCHANGE` delivered to FrameInk. It reported the interruption, finalised the valid portion, rejected PNG against the invalid selection and accepted a fresh selection. |

## Not verified

- Live monitor transfers, negative-origin desktop placement and mixed-DPI changes with multiple monitors. The available session exposed one display. A native topology or DPI change was not performed. The smoke test validates the display-change handling with a simulated message, not the full OS transition.
- Capture from a GNOME Remote Desktop client window. No GNOME session with synthetic or sanitised content was available for this validation, so the smoke test used a local synthetic window instead.
- Windows 10 or Windows N runtime, protected-content examples, long or high-resolution recording performance, and injected encoder or filesystem failures.
