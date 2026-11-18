# Architecture

Status: v0.1.0 (skeleton). This document grows with each milestone.

## Layers

```
            ┌────────────────────────────── LingoCue.App (Avalonia, MVVM, composition root) ─┐
            │                                                                                  │
   ┌────────┴────────┐   ┌──────────────────────┐   ┌──────────────┐   ┌──────────────────────┐
   │ LingoCue.Media  │◄──│ LingoCue.Transcription│   │ LingoCue.Llm │   │ LingoCue.Infrastructure│
   │ FFmpeg/ffprobe  │   │ Whisper.net, Gemini   │   │ IChatClient  │   │ OS paths, keychain,    │
   └────────┬────────┘   └───────────┬──────────┘   └──────┬───────┘   │ settings, downloads    │
            │                        │                     │           └───────────┬──────────┘
            └────────────────────────┴──────────┬──────────┴───────────────────────┘
                                                ▼
                                       LingoCue.Core
                     domain model · subtitle formats · segmentation · interfaces
```

- **Core** has no project dependencies and no UI dependency. It defines the interfaces that the
  outer layers implement (`IPlatformInfo` today; `ITranscriptionEngine`, `ISecretStore`, ... later).
- **Infrastructure** is the only place that branches on the operating system.
- **App** wires everything up through `Microsoft.Extensions.Hosting` (`ServiceCollectionExtensions`)
  and resolves windows and view models from DI.

A layering test (`tests/LingoCue.Core.Tests/Architecture`) fails the build if a library references Avalonia.

## Startup

1. `Program.Main` creates `PlatformInfo` and a bootstrap Serilog logger (file sink in the platform log folder).
2. A generic host is built: `appsettings.json` configuration, Serilog as the logging provider,
   `AddLingoCue` service registrations.
3. Avalonia starts with `AppBuilder.Configure(() => new App(services))`; `App` resolves `MainWindow`.
4. Unhandled exceptions in `Main` are logged as fatal; logs are flushed on exit.

## Media tools (M1)

- `IMediaToolLocator` (Media) finds `ffmpeg`/`ffprobe`: configured path (`Media:FfmpegPath`) →
  `IPlatformInfo.ToolSearchDirectories` (app folder, app-data tools folder, winget/scoop/choco on
  Windows, Homebrew/MacPorts on macOS) → `PATH`. Finder-launched macOS apps have no shell PATH,
  hence the explicit directories.
- `IProcessRunner` runs tools with an argument list (no shell quoting) and kills the process tree on cancellation.
- `IMediaProbe` parses `ffprobe -print_format json` into `MediaInfo` (duration, video incl. display
  rotation for phone videos, audio streams with language).

## Playback (M1)

`IVideoPlayer` (App/Playback) is the only playback API the UI sees. `MpvVideoPlayer` wraps libmpv
(HanumanInstitute.LibMpv). The `MpvView` control creates the mpv context and hands it to
`PlayerViewModel.Mpv` (one-way-to-source binding) which attaches it to the player. mpv property
events (`time-pos`, `duration`, `pause`) are marshalled to the UI thread. See
[ADR 0001](adr/0001-video-player.md) for the decision and native library distribution.

Opening a video: picker / drag-drop / command line → `MainViewModel.OpenFileAsync` →
`IMediaProbe` validation → `IVideoPlayer.OpenAsync` (paused on the first frame).

## Data model (planned, M2)

`Project` → `Track` (language, kind: Source / Corrected / Translation) → `Cue` (id, start, end, text,
speaker, flags) → `Word` (ASR word with timing). Saved as JSON with the `.lingocue` extension, ASR
words cached inside.

## Preview equals output (planned, M1/M5/M6)

The ASS style model is the single source of truth. The preview loads a temporary ASS file into libmpv
(libass); burn-in uses FFmpeg's `ass`/`subtitles` filter on the same file with `fontsdir`.
