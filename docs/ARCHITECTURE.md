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

## Language model passes (M4)

```
track cues ──► LineItem(id, text, duration, speaker, locked)
                 │
  ContextExtractor (1 call) ──► ProjectContext + glossary suggestions (merged, marked "suggested")
                 │
  LineBatchRunner: batches of 30, ≤2 in parallel, 4 previous / 2 next lines as context
     prompt = task rules + context + glossary + {"lines":[{id,text}]}   ── JSON schema output
     validate: ids, count, empty, line limit, locked glossary terms  ──► retry with the problems
     transient 429/5xx ──► backoff 2/5/15/45 s;  last attempt: keep valid lines, rest = Failed
     optional reflect pass (source + drafts) and shorten pass (lines above max CPS)
                 │
  TrackPassRunner ──► new track (Corrected / Translation), cues cloned with the same timing,
                      SourceCueId links, NeedsReview / LlmFailed marks
```

- `ILlmProvider` (Llm) lists models, prices them and creates a `Microsoft.Extensions.AI.IChatClient`;
  `GeminiProvider` wraps `Google.GenAI`. The pipeline only sees `IChatClient`, so tests run against a
  scripted fake client and another provider can be added without touching the pipeline.
- Model names are never hard-coded: `ModelCatalog` filters the live list to text models and picks
  the newest stable Flash (fast) and the newest Pro not older than that Flash (quality);
  `ModelResolver` caches the list and honours explicit choices from settings.
- Tasks (`CorrectionTask`, `TranslationTask`, `ShortenTask`) own the instructions and the per-line
  validation; a correction that changes more than 60 % of a line is kept but marked NeedsReview.
- Settings (`AppSettings`, Core) are stored by `JsonSettingsStore` (Infrastructure) in the app-data
  folder; secrets stay in `ISecretStore`. Glossary and context are part of the project file.
- The editor links a derived track to its source through `SourceTrackId`/`SourceCueId` to show the
  original text and the changed-only filter.

## Gallery, rounded box, doubtful words, Neural Engine, cache (v1.1.0)

- **MainViewModel** is split by area into partial files: Open, Export, Transcription, Passes, Welcome, History, Updates and Help. Logic without UI lives in libraries with tests:
  - `PassPlanning.Options` and `AddTrack` (Llm),
  - `AsrTracks.Add` and `SubtitleExportNames` (Core).
- **Rounded block box.** libass cannot round its BorderStyle 4 box, so `AssScriptBuilder` draws a `\p1` rounded rectangle one layer below the text when it is given an `ITextMeasurer`.
  - `FontTextMeasurer` finds the face libass will use (`IFontCatalog.FindFace`: family, bold, italic, project fonts first).
  - It sums `hmtx` advances through `cmap` and scales by `size / (usWinAscent + usWinDescent)`, as libass does (`FontMetrics`).
  - Lines it cannot measure, or wider than the margins, use the `"<style> Box"` fallback style (square BorderStyle 4).
  - The preview, the export jobs, the ASS export and the thumbnails all pass a measurer.
  - `RoundedBoxTests` renders both boxes with FFmpeg and compares their edges.
- **Template gallery.** `StyleThumbnails` (Media) writes the template's script (`AssScriptBuilder`, one line spanning 0–1 h because input seeking resets frame time).
  - It renders one frame with FFmpeg's `ass` filter (or over `Assets/gallery-background.jpg` when there is no video), crops to the subtitle area and caches by script and frame.
  - `StyleTemplates.LooksLike` marks the applied card and tolerates the colour pickers' HSV rounding.
- **Doubtful words.** `CueViewModel.DoubtfulWords`: words under `AsrReview.DoubtfulConfidence` on a recognized track while the line waits for review. The timeline draws a bar at each word's time; the editor's word buttons call `PlayWord`.
- **Neural Engine.** `CoreMlEncoder` downloads the whisper.cpp Core ML encoder (pinned SHA256) and unpacks it next to the model under whisper.cpp's name (quantization suffix dropped).
  - Turning it off renames the folder to `.off`, so whisper.cpp falls back to Metal.
- **Prompt cache.** `LlmUsage.CachedInputTokens` comes from `UsageDetails.CachedInputTokenCount`. `LlmPrice.Cost(LlmUsage)` charges cached input at `CachedInputShare` (25 %). `PromptCacheTests` guards that every batch shares the same system prompt.

## Packaging, updates and diagnostics (v1.0.0)

- **Packages.** `release.yml` builds both platforms on a `v*` tag.
  - Windows: `build/windows/pack.ps1` runs Velopack's `vpk pack`. It produces a per-user
    `LingoCue-win-Setup.exe`, a portable zip, full and delta `.nupkg` files and `releases.win.json`. The
    previous release is fetched first (`vpk download github`) so the new version gets a delta.
  - macOS: `make-app.sh` builds the bundle (with `AppIcon.icns` made by `sips` + `iconutil` from
    `build/appicon/lingocue-1024.png`); `make-dmg.sh` wraps it in a UDZO image with an Applications link.
  - Release notes for both come from `CHANGELOG.md` (`build/release-notes.sh`).
- **Velopack hooks.** `VelopackApp.Build().Run()` is the first line of `Main`. During install, update
  and uninstall, Velopack starts the app with hook arguments and the process exits inside `Run()`.
- **Updates.** `IUpdateService` (Core) has one implementation, `UpdateService` (Infrastructure):
  - On a Windows copy installed by Setup.exe it uses Velopack's `UpdateManager` with `GithubSource`:
    check, download in the background, `ApplyUpdatesAndRestart`.
  - Everywhere else (macOS is unsigned, the portable zip, dev builds) it reads GitHub's `releases/latest`
    and compares versions with `ReleaseVersions.IsNewer`. The UI then offers the release page; only
    github.com pages are opened.
  - `UpdateSettings` keeps the auto-check flag, the last check (at most every 20 h) and a skipped
    version.
  - `LINGOCUE_UPDATE_SOURCE` points the updater at a local folder to test an update end to end.
- **Diagnostics.** `DiagnosticsService` (App) writes a zip with a summary, the five newest logs and
  `settings.json`. `DiagnosticsReport.Redact` replaces the home folder in every form (both slash
  styles, JSON-escaped) with `~`; `SanitizeSettings` drops the recent-files list.
  `Dispatcher.UIThread.UnhandledException` is handled: logged, shown with an offer to make the package.

## Background box, hardware and Whisper accuracy (v0.12.0)

- **Background box.** `SubtitleStyle.Background` (`SubtitleBackground`) is LingoCue's own setting,
  not part of the ASS style line. `AssScriptBuilder` turns it into ASS:
  - A line box is BorderStyle 3 painted with the box color, padded by `\xbord`/`\ybord`.
  - A block box is libass's BorderStyle 4: one rectangle per event, painted with BackColour and
    padded by `\xshad`/`\yshad`.
  - A frame is a copy of the event one layer below, with invisible glyphs and a larger box in the
    frame color.
  - A band is a full-width `\p1` rectangle sized from the line count (`LineCount`).
  - With any of these background events, layers are spread (text 2L+1, background 2L) so libass
    never pushes a box and its text apart.
  - `SubtitleBackground.Of` reads a plain BorderStyle 3/4 style (older projects, imported ASS) as
    the matching background.
- **Hardware.** `IHardwareInfoProvider` (Infrastructure) reads the adapter with the most memory from
  the display class registry key on Windows, and the chip and memory through `sysctl` on macOS.
  `HardwareAdvisor` maps usable GPU memory (two thirds of unified memory on Apple Silicon) to a
  `HardwareAdvice`: Whisper size, beam search, local LLM size and local batch settings.
- **Whisper device.** `WhisperDevice.Describe` reads whisper.cpp's log (`using … backend`,
  `Core ML model loaded`, `no GPU found`). The runtime name alone is not enough: the macOS runtimes
  fall back from CoreML to Metal and from Metal to the CPU without failing.
  `Whisper.net.Runtime.Metal` ships the Metal shader both macOS runtimes need.
- **Speech-only decoding.** `SpeechTimeline` packs the Silero VAD regions (padded 0.3 s, merged when
  closer than 1 s, 0.6 s pause between) into one recording. Whisper decodes that, and every word
  time is mapped back with `ToSource`. Segments outside speech are still dropped as before.
- **Accuracy options.** `TranscriptionRequest.BeamSearch` (beam 5) and `Hints` (from
  `AsrReview.SpellingHints`, appended to the style prompt, capped at 180 characters and carried to
  every window). `WhisperHallucinations` gives known invented phrases, loops and unsure "silent"
  windows zero confidence. `AsrReview.FlagDoubtful` marks lines with a word under 0.25 for review.
- **Encoders.** `VideoEncoder.Amf` and `Qsv` are probed with a one-frame encode like NVENC. Any
  failure means "not this machine", so they are simply not offered.

## Languages, free tiers and the local engine (v0.11.0)

- **Per-language reading rules.** `ReadingRulesSettings.ToRules(language)` gives English +3 CPS. For
  Korean it gives `KoreanRules`: 16 characters per line, 10/12 CPS and `LengthCounting.HalfWidthLatin`.
  `ReadingRules.TextLength/LongestLine/Cps` count by that mode, and every length check (warnings,
  QA, segmentation, the shortening pass, the CPS chip) goes through them.
- **Providers.** `ModelResolver` holds every `ILlmProvider` (Gemini, Ollama) and picks the one in
  `LlmSettings.Provider`; dialogs can override it for one run.
  - `OllamaProvider` and `OllamaChatClient` call Ollama's `/api/tags` and `/api/chat` with
    `HttpClient` and source-generated JSON. A JSON-schema response format becomes Ollama's `format`.
  - Local runs use batches of 10, one at a time, with a 10-minute timeout.
- **Unanswered batches.** `LineBatchRunner` catches only "no answer" errors: transient errors after
  their retries, or `LlmException.IsUnanswered`.
  - A batch with no answer is split in half and retried; a single line that still fails is marked
    failed, and the run goes on.
  - After `MaxConsecutiveFailures` the remaining lines are marked without calling the model.
  - Other errors, such as a bad key, still stop the run.
  - `ResilientChat` waits as long as a rate limit asks (`RetryHint`, capped by `MaxRetryWait`).
  - In free-tier mode a shared `RequestPacer` spaces the requests.
- **Retrying failed lines.** `TrackPassRequest.OnlySourceCueIds` runs a pass on some source lines
  only. `TrackPassRunner.MergeInto` writes the results into the existing derived track by
  `SourceCueId`.
- **Hangul fonts.** `FontFileReader` checks a few common syllables in the `cmap` table
  (`FontFace.HasHangul`). `FontCatalog.SuggestHangulFamily` prefers Noto Sans KR, then the
  operating system's Korean fonts. The bilingual second style has its own font name.

## Look and feel (v0.10.0)

- **One place for the design.** `App.axaml` gives the Fluent theme a palette per theme variant
  (`ColorPaletteResources`: accent, window, text). `Styles/Shell.axaml` holds three things:
  - the app's tokens for both variants: surfaces (`AppBackground` → `Chrome` → `Panel` →
    `Sunken`), text, accent and the ok/warning/error pairs;
  - the icon geometries (Material Design Icons, used through `PathIcon`);
  - the component classes, for example `Border.panel/.well/.chip/.tile`,
    `Button.accent/.icon/.small/.step/.card`, `RadioButton.choice`, `ListBox.cues` and
    `TabControl.segmented`.
- **How views use it.** Views reference tokens with `DynamicResource`, so switching the theme
  restyles everything. `TimelineControl` draws in code, so it reads the same tokens through
  `TryFindResource` and rebuilds its brushes when the theme variant changes.
- **Line list.** The lines are a virtualized `ListBox` with a row template rather than a
  `DataGrid`. The window syncs its selection in code-behind, because an unloaded tab must not
  clear the editor's selected line.

## Editor polish (M8)

- **Undo/redo:** `ProjectHistory` (Core) keeps up to 100 JSON snapshots of the project, without the
  bulky ASR runs, which are carried over on restore. `MainViewModel` records a snapshot 400 ms
  after the last change. `SubtitleEditorViewModel.Edited` carries a coalescing key:
  - typing uses `"edit"`, so quick keystrokes merge into one step;
  - nudging the same line repeatedly shares one key;
  - drags, replace-all and quick fixes get a unique key or `null`, so each is its own step.
- **Editing operations** are pure Core functions over a `SubtitleTrack`, tested without UI:
  - `CueEditing.Split`, `MergeWithNext`, `Nudge` and `ClampDrag`, with a 40 ms minimum gap and a
    200 ms minimum length;
  - `QualityCheck.Check` with `FixOverlaps` and `ExtendForReadingSpeed`;
  - `TextSearch`, which uses tr-TR case-insensitive comparison.
- **Waveform:** `WaveformService` (Media) works in four steps:
  1. probes the file;
  2. decodes the first audio stream to 8 kHz mono PCM in a temporary file;
  3. reduces it to one peak byte per 10 ms;
  4. caches the result in `<app data>/cache/waveforms/<hash>.lcwf`, keyed by full path, size and
     modification time.
- **Timeline:** `TimelineControl` is a code-drawn `Control`, so thousands of lines and samples do not
  each create a visual. It draws only the visible time range.
  - Zoom, scroll and hit-testing live in the control; `TimelineViewModel` supplies the data and the
    actions (seek, select, clamp, apply).
  - A drag shows a ghost outline and seeks at most every 200 ms with a fast keyframe seek. The times
    change once, on release.
- **Shortcuts** are handled in the window's tunnelling key handler:
  - Ctrl/⌘ shortcuts work everywhere;
  - Alt+arrows and `[ ]` are ignored while a text box has focus;
  - `[ ]` match by `KeySymbol`, because on a Turkish keyboard they are AltGr+8/9.

## Translation and tracks (M7)

- Tracks form chains: ASR "Kaynak" → "Düzeltilmiş" → "Çeviri · en". Each derived track has
  `SourceTrackId`, and each of its cues has `SourceCueId`, with the same timing.
  `TrackOperations.OriginalText` follows the links back to the first track.
- Translation sends the edited line as `text` and the recognized line as `original` (only when they
  differ). The prompt says the text is authoritative. For Turkish sources it adds a rule for the
  genderless third-person pronoun.
- `TrackOperations` keeps the links valid:
  - **Delete** relinks the deleted track's children to its own source, remapping cue ids, or unlinks
    them. It also clears the bilingual and active-track references.
  - **SyncTimingFromSource** copies times by `SourceCueId` and counts lines without a counterpart.
  - **AddImported** creates a new track and reads the language from `name.<lang>.srt`.
- Reading rules depend on the language (`ReadingRulesSettings.ToRules(language)`): English gets
  +3 CPS. The editor asks `RulesFor(track.Language)`, and the shortening pass uses the output
  language's rules.

## Style (M5)

```
Project.Style (main ASS style) + Project.Presentation (upper case, fades, karaoke, bilingual)
        │
AssScriptBuilder ──► one ASS script ──► preview (libmpv/libass, sub-fonts-dir = <project>/fonts)
        │                           └─► ASS export, burn-in (M6, FFmpeg fontsdir = same folder)
        ├─ upper case: SubtitleText.MapVisible with the track language's culture (tags untouched)
        ├─ fades: {\fad(in,out)}
        ├─ karaoke: KaraokeTiming aligns the cue's text to its ASR words (Needleman–Wunsch on
        │  normalized spelling; unmatched words share their neighbours' time) →
        │  Fill = {\kf} per word, ActiveWord = one event per word with the word recolored
        └─ bilingual: second track in style "Secondary" on layer 0, main track on layer 1;
           each event's MarginV comes from the estimated height of the other track's text shown
           at the same time, so libass never shifts either block
```

- Styles are authored on a 1080-pixel-high canvas; `PlayResFor` keeps that for any aspect ratio.
- Templates (`StyleTemplates`, Core) are a style plus presentation options without track references;
  user templates are JSON files in `<app data>/templates` (`JsonStyleTemplateStore`, Infrastructure).
- Fonts: `FontFileReader` reads the `name` and `cmap` tables of TTF/OTF/TTC files, so the font list
  uses the legacy family names that ASS and libass use (Avalonia's font list groups faces such as
  "Arial Black" under "Arial"). `FontCatalog` scans `IPlatformInfo.FontDirectories` and the project's
  `fonts` folder and reports missing Turkish glyphs.
- The style panel (`StyleEditorViewModel`) writes every edit to the project at once and raises
  `Changed`; the main view model marks the project dirty and refreshes the preview.
- XAML rule: `Run.Text` binds two-way by default in Avalonia; display-only Runs must say
  `Mode=OneWay` (enforced by `XamlRulesTests`).

## Export (M6)

```
ExportDialog ──► subtitle files: SubtitleFile.WriteAsync (SRT/VTT, or ASS from AssScriptBuilder)
            └──► video: ExportJobs (Media) ── snapshot now ──► <temp>/LingoCue/jobs/<id>/
                       │                                         subtitles.ass | trackN.srt/ass
                       │                                         fonts/ (copy of <project>/fonts)
                       ▼
                 FfmpegCommands ──► RenderQueue (App, one at a time) ──► VideoRenderer (Media)
                   burn-in: ass=subtitles.ass:fontsdir=fonts, x264 | NVENC | VideoToolbox,
                            audio copy or AAC, -fps_mode passthrough, +faststart
                   soft sub: -c copy + mov_text (MP4) or ASS + attached fonts (MKV)
```

- FFmpeg runs inside the job folder, so the `ass` filter only sees relative names. Escaping Windows
  paths (drive colons, backslashes) inside a filter graph is fragile, and this avoids it.
- `VideoRenderer` writes `<name>.lingocue-partial.<ext>` and renames it on success. Failure or
  cancellation (the process tree is killed) deletes it, so a half file never sits under the real name.
- Hardware encoders are found with a one-frame test encode using the export's own settings.
  `HardwareEncoderStatus` separates "not on this machine" (not offered) from "present but unusable"
  (shown with a reason, e.g. an old NVIDIA driver).
- Progress comes from `-progress pipe:1` (`out_time_us`, `speed`), giving a fraction, speed and time left.
- The job snapshot means editing the project while a render waits does not change that render.

## Preview equals output

The ASS style model is the single source of truth. `AssScriptBuilder` produces the script for both
the live preview (libmpv/libass) and, from M6, burn-in (FFmpeg `ass` filter with `fontsdir`).
The script is sized to a 1080-pixel reference following the video aspect (1920×1080 landscape,
1080×1920 Shorts), so style sizes mean the same thing on every video.

## Mixed-language recordings (v1.2.0)

Decision record: [ADR 0003](adr/0003-mixed-language-recognition.md).

```
"tr+en+ko" ──► Silero VAD regions ──► whisper base (60 MB) detects each region among the chosen languages
                                          │  unsure (p < 0.8) → large-v3 rechecks that region
                                          ▼
                    LanguageRuns.Smooth (unsure short regions follow neighbours) → Group by language
                                          │
                     one decoding pass per language over its regions (SpeechTimeline, own prompt)
                                          ▼
                     words tagged Word.Language ──► Segmenter breaks at language switches → Cue.Language
```

- `LanguageSelection` (Core) stores the choice as one string: `auto`, `tr`, or `tr+en+ko` (at most 3).
- `Cue.Language` and `Word.Language` are optional. Null means the track's language; use
  `SubtitleTrack.LanguageOf(cue)`. The track's own language is the first chosen language when it was spoken.
- `TextLanguageGuess` (Core) picks a line's language among candidates from its script and common words. It
  flags Whisper segments written in another chosen language and tags Gemini output, which has no per-word
  language.
- Per-line language is used by:
  - reading rules: `SegmentationOptions.RulesFor`, and the editor rows;
  - upper case: `AssScriptBuilder.CultureOf`;
  - the LLM passes: `LineItem.Language` is sent as `"lang"`, correction rejects a translated line, and
    translation locks lines already in the target language;
  - export: `TrackOperations.SplitByLanguage`.

## Subtitle plan and Premiere overlay (v1.3.0)

Decision record: [ADR 0004](adr/0004-premiere-overlay-and-master-formats.md).

- `SubtitlePlan` (Core, in `PresentationOptions.Plan`) holds one `LanguageRule` per spoken language. Each rule
  has a top and a bottom `PlanLine`: None, Original, or Translation(language).
- `SubtitlePlans.Resolve` turns the shown track into top and bottom cue lists:
  - Translations come from the project's translation tracks, matched by `SourceCueId`.
  - Missing translations are reported as `MissingTranslation`.
  - `AssScriptBuilder` feeds the two lists into the existing bilingual layout.
  - `Flatten` writes both lines into one cue for SRT/VTT/MP4.
- `TrackPassRunner.MergeOrAdd` fills the missing translations: it sends only those lines and adds or updates
  them in the translation track.
- Overlay:
  - `FfmpegCommands.Overlay` draws onto a transparent canvas: `color=black@0`, then `ass … :alpha=1`, then
    qtrle or ProRes 4444 in a MOV.
  - The canvas uses the source's exact frame rate (`VideoStreamInfo.RateForFfmpeg`).
  - `ExportJobs.WithExactColors` adds `YCbCr Matrix: None` so the colors are drawn as RGB.
- Burn-in `VideoFormat`:
  - H.264, HEVC Main10 (hardware when `HardwareEncoderStatus.Hevc10`, else x265), or ProRes 422 HQ.
  - 10-bit formats insert `format=…10le` before `ass`.
  - `ColorArguments` copies the source's color tags.
