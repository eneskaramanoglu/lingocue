# 0002. Render the mpv video view through OpenGL on Windows too

- Status: accepted (amends the renderer choice in [0001](0001-video-player.md))
- Date: 2026-11-18

## Context

ADR 0001 kept HanumanInstitute.LibMpv's default on Windows: `NativeView`, a Win32 child window that
mpv draws into directly. While building the M2 editor (cue table, edit panel, project open) the
video area stayed **black** whenever a project was opened: the window layout changed (the cue
table filled, panels resized) between creating the native child window and the first frame, and
the child window lost its picture. Opening a bare video, without the table changing, still worked.
Audio and the playback clock kept running, so only the presentation was broken.

A native child window also has two known limits on Windows: Avalonia cannot draw anything on top
of it ("airspace"), and it sits outside Avalonia's layout and hit testing.

## Decision

Use `Renderer="OpenGl"` for the `MpvView` on every platform. On Windows Avalonia already renders
through ANGLE/EGL (set in `Program.cs`), so mpv renders into an Avalonia OpenGL surface
(`OpenGlControlBase`) exactly like on macOS.

Verified on Windows 11 (NVIDIA): video shows after opening a project, a bare video and after
layout changes; playback, seeking, frame stepping and the live ASS preview (Turkish glyphs,
italics) work; audio is unaffected.

## Consequences

- One rendering path for Windows and macOS: fewer platform-specific surprises.
- Avalonia controls may be drawn over the video from now on (e.g. a future subtitle drag handle);
  the "no overlays on the video" rule in CLAUDE.md is lifted.
- The OpenGL view uses `hwdec=auto-copy` on Windows (decoded frames are copied back from the GPU),
  slightly more CPU than zero-copy. Irrelevant for preview of 1080p; revisit only if 4K preview stutters.
- If a GPU/driver cannot create an ANGLE context, Avalonia falls back to software rendering and the
  view may be black; the `Software` renderer of LibMpv is the next fallback to wire up if that is reported.
