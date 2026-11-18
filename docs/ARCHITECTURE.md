# Architecture

Status: v0.4.0 (transcription). This document grows with each milestone.

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

## Data model (M2)

`Project` → `SubtitleTrack` (language, kind: Source / Corrected / Translation, source track id) →
`Cue` (stable id, start, end, text, speaker, `CueMarks`, source cue id, format data) → `Word`
(ASR word with timing and confidence). `SubtitleStyle` is the ASS V4+ style record;
`AssScriptInfo` holds the script resolution.

- Times are millisecond `TimeSpan`s. ASS only stores centiseconds, so an ASS round-trip may move a
  time by up to 5 ms; the project file keeps exact milliseconds.
- Cue text uses `\n` line breaks and keeps inline formatting as written (`<i>` from SRT/VTT or
  `{\i1}` from ASS). `TagConverter` translates between the two when writing another format.
- `ReadingRules` (42 chars/line, 2 lines, 0.8–7 s, 17 CPS target / 21 max) evaluates each cue into
  `CueIssues`; the UI colors CPS and lists problems. Segmentation (M3) will use the same rules.

### Formats (`LingoCue.Core.Formats`)

`ISubtitleFormat` implementations: `SrtFormat` (lenient), `VttFormat`, `AssFormat` (also reads SSA
v4 styles). `SubtitleFile` detects the format by content first, decodes BOM → strict UTF-8 →
Windows-1254 (legacy Turkish), and writes UTF-8 (optional BOM, CRLF/LF) atomically.

### Project file (`.lingocue`)

Indented UTF-8 JSON via `ProjectSerializer`: `schemaVersion` (newer versions are refused), times
as integer milliseconds, colors `#RRGGBB[AA]`, enums as strings, computed values not stored. The
video path is relative to the project file with forward slashes so a project folder can move
between Windows and macOS.

## Editor (M2)

```
DataGrid (CueViewModel rows) ── SelectedCue ── edit panel (start/end/text/speaker)
        ▲                            │
        │ Position (follow)          │ Seek on user selection
   IVideoPlayer ◄────────────────────┘
        ▲
        │ sub-add / sub-reload (debounced 300 ms)
SubtitlePreviewService ── AssScriptBuilder(project, active track, video size) → %TEMP%/LingoCue/preview-<pid>.ass
```

`SubtitleEditorViewModel` owns the rows of the active track and keeps model and list sorted by
start time; every edit raises `Edited`, which marks the project dirty and refreshes the preview.
`MainViewModel` owns the project lifecycle (open video/project/subtitle, save, export, unsaved
prompt) and `OpenFileAsync` routes picker, drag-drop and command-line paths by extension.

## Transcription (M3)

```
media ──ffmpeg──► 16 kHz mono WAV ──► Whisper.net (CUDA → Vulkan → CPU | CoreML → CPU)
   │                                      │  Silero VAD drops segments outside speech
   │                                      ▼
   └──ffmpeg──► FLAC chunks ≤25 min ──► Gemini 3.5 Transcribe (Files API + Interactions API)
                 (cut at silences)        │  verbatim, word timestamps, optional speakers
                                          ▼
                    words (text, start, end, confidence, speaker) ── cached in Project.AsrRuns
                                          │
                                   Segmenter (Core) ──► cues of a new Source track
```

- `ITranscriptionEngine` (Transcription) is the only contract the UI uses; engines report
  `TranscriptionProgress` stages and honor cancellation. `TranscriptionException.Remedy` carries
  a Turkish, user-facing fix.
- Whisper models come from a pinned commit of `ggerganov/whisper.cpp` on Hugging Face
  (`WhisperModels`), downloaded by `ModelDownloader` (resume via HTTP Range, SHA256 check) into
  `<app data>/models`. Default `large-v3-q5_0` (1.1 GB) fits 4 GB GPUs; `large-v3-turbo-q5_0` is
  the fast option.
- Timing comes **only** from the ASR engine. LLM passes (M4) change text, never times.
- `Segmenter` (Core) turns words into cues: hard breaks at speaker changes, pauses > 400 ms and
  sentence ends; length/duration limits resolved at the best break (after punctuation, before
  conjunctions); Turkish heuristics keep determiners, numbers, frequent adjectives and genitive
  noun compounds with the next word; balanced two-line wrapping; durations extended toward the
  reading-speed target without overlap.
- API keys live in `ISecretStore` (Windows Credential Manager via advapi32, macOS Keychain via
  Security.framework); `ApiKeyProvider` lets `GEMINI_API_KEY` override for tests/CI.

## Preview equals output

The ASS style model is the single source of truth. `AssScriptBuilder` produces the script for both
the live preview (libmpv/libass) and, from M6, burn-in (FFmpeg `ass` filter with `fontsdir`).
The script is sized to a 1080-pixel reference following the video aspect (1920×1080 landscape,
1080×1920 Shorts), so style sizes mean the same thing on every video.
