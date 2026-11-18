# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.4.0] - 2026-11-18

### Added
- **Transkribe Et** (toolbar, Altyazı menu, Ctrl/⌘+T): turns the speech in a video into a new
  source track with word-timed, readable cues.
- Local engine **Whisper** (Whisper.net 1.9.1): CUDA → Vulkan → CPU on Windows, CoreML on macOS; the
  device in use is shown, with a hint when an NVIDIA GPU could use CUDA. Models large-v3 q5_0
  (default, 1.1 GB) and large-v3-turbo q5_0 (0.6 GB), downloaded on first use from a pinned
  whisper.cpp commit with resume and SHA256 verification. Silero VAD drops hallucinated segments.
- Cloud engine **Gemini 3.5 Transcribe**: verbatim mode with word timestamps and optional speaker
  diarization; audio uploaded as FLAC, split at silences beyond 25 minutes; retries on rate limits;
  cost estimate (~$0.005/min).
- Segmentation into cues: speaker changes, pauses, sentence ends, 42×2 characters, 0.8–7 s, reading-speed
  target; Turkish heuristics keep determiners, numbers, frequent adjectives and genitive compounds
  with the following word.
- Recognition results cached in the project; re-running with the same settings is free and instant.
- **Ayarlar → API Anahtarları**: Gemini key stored in Windows Credential Manager / macOS Keychain;
  `GEMINI_API_KEY` overrides it.
- Progress (stage, percent, device) and **İptal** in the status bar; recognition runs off the UI thread.
- Track selector when a project has more than one track.
- Audio extraction (WAV/FLAC, slices, progress) and silence detection in the media layer.

### Changed
- New test project `LingoCue.Transcription.Tests`.

## [0.3.0] - 2026-11-18

### Added
- Subtitle domain model: project, tracks (source/corrected/translation), cues with stable ids,
  speakers, review marks, ASR words; ASS style and script info.
- SRT, WebVTT and ASS readers/writers with round-trip tests. SRT reading is lenient (BOM, CRLF,
  missing indexes, `.` separators, blank lines in text); VTT keeps identifiers, settings, STYLE/REGION
  blocks and voice tags; ASS keeps styles, layers, margins, effects and reads legacy SSA styles.
- Inline formatting conversion between SRT/VTT tags and ASS overrides (libass would print `<i>` literally).
- Encoding detection on import (BOM, UTF-8, Windows-1254 for legacy Turkish files); UTF-8 export
  with optional BOM and CRLF/LF; atomic writes.
- `.lingocue` project files: readable JSON, schema version, relative video path, millisecond times.
- Cue table with start/end/duration/CPS/text/speaker, CPS warning/error colors and issue tooltips,
  current-cue marker, follow playback, click to seek.
- Cue editor: start/end time entry, multi-line text, speaker, set start/end to playhead, add/delete.
- Live subtitle preview on the video (debounced ASS regeneration + mpv `sub-reload`), sized to a
  1080 reference that follows the video aspect (Shorts stay 1080 wide).
- File menu: open video/project, save/save as, import and export SRT/VTT/ASS; shortcuts Ctrl/⌘+O, S,
  Shift+S, I; drag-and-drop and command line accept videos, subtitles and projects.
- Unsaved-changes prompt on close and before replacing the project; "•" in the title when dirty.
- Readability rules (42 chars/line, 2 lines, 0.8–7 s, 17/21 CPS) shared with the upcoming segmentation.

### Changed
- The mpv video view renders through OpenGL on Windows as well (ADR 0002); fixes a black video area
  after the layout changed.
- The main window opens maximized.

### Removed
- The developer-only `--subtitle` command-line flag (subtitles are imported from the UI now).

## [0.2.0] - 2026-11-18

### Added
- Embedded video player based on libmpv (HanumanInstitute.LibMpv 0.10.1) behind an `IVideoPlayer`
  abstraction; decision recorded in ADR 0001.
- Open videos (MP4, MOV, MKV, M4V) with **Video Aç** / Ctrl+O / ⌘O, drag-and-drop, or as a command-line argument.
- Transport controls: play/pause, 5 s jumps, frame stepping, seek slider, `hh:mm:ss.fff` timecode;
  shortcuts Space, ←/→, Shift+←/→.
- ffprobe-based media inspection (duration, resolution, frame rate, codec, audio tracks, phone rotation)
  shown in the status bar; files are validated before playback.
- FFmpeg/ffprobe discovery across settings, app folders, winget/Scoop/Chocolatey, Homebrew/MacPorts and PATH,
  with per-OS install instructions when missing.
- External ASS subtitle preview through libass (developer flag `--subtitle`, UI in v0.3.0).
- Media integration tests that generate sample clips with FFmpeg at test time.

### Changed
- Avalonia renders with ANGLE/EGL on Windows and OpenGL on macOS (required by the video view).
- Windows package bundles `libmpv-2.dll` (pinned shinchiro build, SHA256-verified, downloaded at build time);
  the macOS `.app` bundles libmpv and its dependencies in `Contents/Frameworks`.
- CI installs FFmpeg on both runners and fails (instead of skipping) when media tests cannot run.

## [0.1.0] - 2026-11-18

### Added
- Solution skeleton on .NET 10: `Core`, `Media`, `Transcription`, `Llm`, `Infrastructure`, `App`
  plus `Core`, `Llm`, `Media` test projects (xUnit v3, Shouldly, Microsoft.Testing.Platform).
- Avalonia 12 main window with the planned layout (toolbar, video + timeline, cue table, status bar
  with progress/cancel) and System/Light/Dark theme switching.
- Dependency injection via the .NET generic host, Serilog rolling file logs in the per-user log folder.
- `IPlatformInfo` abstraction; OS checks live only in `LingoCue.Infrastructure`.
- Turkish casing helpers (`TurkishText`) with tests for i/İ and ı/I.
- Layering test ensuring core libraries never reference Avalonia.
- Central package management, nullable, warnings-as-errors, `.editorconfig`, repo-local `nuget.config`.
- CI on `windows-latest` and `macos-latest`; tag-driven release workflow producing win-x64 zip and
  unsigned osx-arm64 `.app` zip.
- Docs: plan, research notes, architecture overview, macOS test checklist, `CLAUDE.md`.

[Unreleased]: https://github.com/eneskaramanoglu/lingocue/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/eneskaramanoglu/lingocue/releases/tag/v0.1.0
