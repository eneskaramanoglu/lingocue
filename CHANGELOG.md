# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/eneskaramanoglu/lingocue/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/eneskaramanoglu/lingocue/releases/tag/v0.1.0
