# FrameInk MVP specification

**Goal:** A LICEcap-style native Windows recorder that can mark up a fixed screen region live and save an annotated PNG without interrupting a recording. The user runs the app on Windows, not WSL. They may capture content displayed inside a GNOME Remote Desktop client window on Windows.

## User workflow
1. Open a persistent, movable, resizable capture frame. Its attached toolbar sits outside the capture interior and remains usable. Select the region by positioning the frame over the desktop, not a one-shot snip picker. Show the active capture dimensions.
2. Interact mode (default): the interior is click-through, including where annotation pixels are visible. Draw mode: the interior accepts drawing input. Switching modes preserves marks and doesn't stop a recording. Start with pen, arrow, rectangle, numbered step marker, undo and clear.
3. Save a PNG via the toolbar or a configurable global hotkey, before or during recording. The PNG uses the current region and active marks, contains no recorder chrome, and does not stop the take. Save locally and confirm the path without introducing UI into the output. Report hotkey collisions cleanly.
4. Start/stop local MP4 recording via the toolbar. Lock frame position and dimensions during a take but keep drawing, mode switching, snapshot and stop available. Outputs include marks at the time each frame was captured, but not the frame border, resize handles, toolbar or recording controls. On stop, finalise a playable file and unlock the frame.

## Implementation direction
- Prefer C#/.NET and WPF with Windows interop where needed. Choose a supported Windows capture path after proving the UI behaves correctly. WSL is a development environment only; run interaction tests on the Windows desktop. Prototype the frame, click-through mode, drawing and PNG first, then add MP4 on the same crop/compositing path.
- Use physical-pixel capture coordinates and a single compositing path for the PNG and video. Keep annotations as separate region-relative data and composite deliberately; prove the Windows capture path doesn't bake in the overlay separately or capture it twice. Validate mixed-DPI positioning on a selected monitor where available. For the MVP, constrain the frame to one monitor at a time if needed and explain the restriction. If display topology or DPI changes would invalidate an active crop, fail visibly rather than silently capturing a different area.
- Capture only on explicit user action. No upload, account, telemetry, background surveillance, microphone or system audio. Don't attempt to capture content Windows protects. Handle capture, encoder and file errors explicitly. GIF export, cross-monitor regions, cloud sync, automatic redaction, installers and publishing are follow-up work.

## Acceptance and handover
- On Windows, place the frame over a known visual target. Verify drag/resize, exact PNG dimensions, no chrome in output, and input reaches the underlying app in Interact mode.
- Draw marks, undo one, switch to Interact, and verify marks persist without intercepting application clicks. Verify toolbar and hotkey PNG capture with the same crop and effective annotations.
- Start recording, draw, switch to Interact, save a PNG while recording, then stop. Verify PNG and corresponding video frames show the same annotations and crop, the recording continues after the PNG, and MP4 plays through. Test with a GNOME Remote Desktop client window if available; otherwise use a local non-sensitive test window and report the remote scenario as not run.
- Include a short build/run README, repository copy of this spec at docs/spec.md, automated tests for crop/compositing where practical, and a Windows smoke-test checklist with actual outcomes. Use synthetic or sanitised evidence only. Return the exact candidate and any unverified acceptance checks. Don't publish or push externally.
