# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.9.0] - 2026-11-20

### Added
- **Undo and redo** (Düzen menu, Ctrl/⌘+Z, Ctrl+Y or Ctrl/⌘+Shift+Z) for every project change:
  text, times, adding/deleting/splitting/merging lines, style, tracks, quick fixes. Up to 100 steps.
  - Typing in one line becomes one step, and a timeline drag is one step.
  - Saving remembers the state, so undoing back to it clears the unsaved mark.
- **Timeline** under the video: the waveform, the lines of the active track and the playhead.
  - Click to seek. Drag a block to move a line, or drag an edge to change its start or end. Lines
    never overlap their neighbours.
  - Ctrl/⌘+wheel zooms around the pointer and the wheel scrolls. The timeline follows playback.
  - The waveform is prepared in the background and cached, so reopening a video shows it at once.
- **Line shortcuts**:
  - Alt+←/→ moves the selected line by 100 ms; with Shift only its start moves.
  - `[` and `]` set the start and end to the playhead (AltGr+8/9 on a Turkish keyboard).
  - Ctrl+B splits a line. While typing it cuts at the caret; otherwise it cuts at the word spoken at
    the playhead, or at the middle.
  - Ctrl+M merges a line with the next one, and Ctrl+Enter adds a new line.
  - Yardım → Klavye kısayolları lists all shortcuts.
- **Kontrol** tab: every problem of the track (reading speed, line length and count, too short or
  long, overlaps, gaps too small to notice). Picking one jumps to the line.
  - Quick fixes: **Çakışmaları gider** and **Okuma hızı için süreyi uzat**. Both can be undone.
- **Bul ve değiştir** (Ctrl/⌘+F): search with Turkish case rules (İ/ı) and an optional whole-word
  match. Enter finds the next match and Shift+Enter the previous one. Replace one or all; replacing
  all is one undo step.
- **Dosya → Son açılanlar**: the last 10 projects and videos.

## [0.8.0] - 2026-11-19

### Added
- **Çevir** (toolbar, Altyazı menu): translates a track into a new track with exactly the same
  timing.
  - The source defaults to the latest "Düzeltilmiş" track, because the edited text is
    authoritative; what speech recognition heard goes along as a hint for each line.
  - Target languages: English (default), Turkish, German, French, Spanish, Italian, Arabic,
    Russian and Azerbaijani. Translating a foreign-language video into Turkish works the same way.
  - Glossary entries for the target language are enforced.
  - Turkish pronouns have no gender, so he/she/they is inferred from the video context and kept
    consistent, or neutral wording is used.
  - The dialog shows the model and a token/cost estimate; an optional review pass gives more
    natural wording.
  - The first translation becomes the bilingual mode's second track.
- **İzler** window (Altyazı menu, or the button next to the track selector, which is now always
  shown): rename, change the language, reorder (export order), duplicate, add an empty track,
  delete (with confirmation), and **Zamanlamayı kaynaktan al**.
  - "Zamanlamayı kaynaktan al" copies timing fixes made in the Turkish track into its translation.
  - Deleting a track keeps the tracks made from it linked to the original lines.
- **Importing a subtitle** into a project that already has lines asks: add it as a new track (the
  language comes from names like `film.en.srt`) or replace the active track.

### Changed
- Reading speed limits depend on the track language: English allows 3 characters per second more
  than Turkish (Netflix guidance: 20 vs 17 CPS). The editor's marks and the shortening pass follow it.
- In a translation track the editor no longer marks every line as "changed"; the filter becomes
  "Kontrol bekleyenler".

## [0.7.0] - 2026-11-19

### Added
- **Dışa Aktar** (toolbar, Dosya menu, Ctrl/⌘+E): one dialog for every export.
  - **Subtitle files:** SRT (with a "Premiere Pro uyumlu" UTF-8 BOM option, on by default, and
    import notes), WebVTT, or ASS with styling, karaoke and bilingual layout. Several tracks are
    written as separate files.
  - **Videoya göm (MP4):** burns the subtitles in exactly as the preview shows them, using the same
    ASS and the same libass renderer, with the project's fonts. H.264 with x264 "veryfast" at a
    quality from "Küçük dosya" to "Çok yüksek kalite" (default CRF 19). The audio is copied, or
    converted to AAC when MP4 cannot hold it. Variable frame rate phone videos keep every frame's
    timing.
  - **Fast hardware encoding:** NVIDIA NVENC on Windows and VideoToolbox on macOS when they work on
    the machine. A one-frame test encode decides, and the dialog explains why one cannot be used
    (e.g. "NVIDIA sürücüsü eski: en az 610 gerekli").
  - **Açılıp kapanabilen altyazılı video:** MP4 (mov_text, plays everywhere) or MKV (ASS tracks
    with styling and attached project fonts). The video is not re-encoded; each track carries its
    language and title, and the first one is the default.
  - Warnings before rendering: a missing font, a font without Turkish letters, an HDR/10-bit
    source.
- **Export queue:** video exports run one after another in the background. "İşler" in the status
  bar shows progress, speed and time left, with cancel per job and "Göster" to open the finished
  file in Explorer/Finder. A failed or cancelled export leaves no partial file. Closing the app
  while exports run asks first.

### Changed
- "Altyazı Dışa Aktar" (SRT/VTT/ASS submenu) is replaced by the export dialog.
- **macOS needs `brew install ffmpeg-full`** instead of `brew install ffmpeg`: Homebrew's plain
  `ffmpeg` no longer includes libass, which burning in subtitles needs. LingoCue prefers
  `ffmpeg-full` when both are installed. With an FFmpeg without libass, the export dialog disables
  "Videoya göm" and explains what to install.

## [0.6.0] - 2026-11-19

### Added
- **Stil** panel (toolbar button or the right panel's Stil tab): font, size, bold/italic/underline,
  letter spacing, upper case, text/outline/shadow colors with transparency, outline or background
  box, 9-point alignment and margins, fade in/out. Every change shows at once in the video preview,
  which is the same libass output that ASS export and (in M6) burn-in use.
- **Templates:** YouTube klasik, Netflix tarzı, Shorts / Reels (9:16) and Belgesel, using fonts
  present on both Windows and macOS. Save the current look as your own template (shared by all
  projects), import/export templates as JSON, delete your own.
- **Karaoke** (word-by-word highlight) from the speech-recognition word timings: "Dolan renk" (words
  fill with the highlight color as they are spoken) or "Aktif kelime" (only the spoken word is
  highlighted, Shorts style). Corrected text is matched back to the recognized words, so karaoke
  also works on the "Düzeltilmiş" track; tracks without word timings stay plain.
- **Bilingual mode:** a second track (e.g. an English translation or an imported SRT) under the
  active one, with its own size and color; the two never overlap (also with top alignment).
- **Fonts:** the font list shows names exactly as the subtitle renderer finds them (e.g. "Arial
  Black"), warns when a font is missing or lacks Turkish letters (ğ ş ı İ …), and "Font dosyası ekle"
  copies a TTF/OTF into a `fonts` folder next to the project, which the preview uses (so a project
  moved to another computer keeps its look).
- Optional safe-area guides (93 % / 90 %) over the video.

### Fixed
- Display-only text in lists could write itself back into the project (e.g. a track's language)
  and, with formatted text, crash the app; such bindings are one-way now.

## [0.5.0] - 2026-11-19

### Added
- **Düzelt** (toolbar, Altyazı menu): an AI pass over the active track that fixes recognition
  errors, proper names, punctuation and spelling by Turkish (TDK) rules. Timing is never touched;
  the result is a new **Düzeltilmiş** track and the source track stays as it was. Lines are sent in
  batches of 30 with neighbouring lines as context, answers are checked (JSON schema, ids, line
  count, locked glossary terms) and retried with the problems listed; lines that still fail keep the
  original text and are flagged. Optional second "reflect" pass and shortening of lines above the
  reading-speed limit. Progress and **İptal** in the status bar; the result reports changed,
  needs-review and failed lines, tokens and the estimated cost.
- Before starting, the dialog shows the model, a token and cost estimate, and whether the model
  has a free tier.
- **Context pass**: one call summarises the video (topic, tone, sen/siz, speakers) and suggests proper
  names and terms for the glossary.
- **Altyazı → Sözlük ve Bağlam**: per-project glossary (term, preferred spelling, kind, note, locked)
  and context, editable; suggestions are marked until accepted.
- **Ayarlar → Tercihler**: default transcription engine, Whisper model and language; LLM fast and
  quality models (automatic = newest stable Flash / newest Pro not older than Flash, picked from the
  provider's live model list), batch size, parallel requests, reflect, shortening, circumflex
  spelling; reading rules; API keys. Settings are stored in `settings.json` in the app-data folder
  (no secrets). A note explains that Google may use free-tier content to improve its products.
- Editor: a mark column shows changed (●), needs-review (!) and failed (✕) lines of a corrected track;
  the original text is shown in the tooltip and under the editor, with **Orijinale dön** and
  **Kontrol edildi**; **Değişenler** lists only those lines. Editing a flagged line clears its flag.
- Gemini provider over `Google.GenAI` (`IChatClient`), with a price table and transient-error backoff;
  translation pipeline infrastructure (UI comes in M7).

### Changed
- Whisper is the default transcription engine again (no API cost while developing and testing);
  Gemini 3.5 Transcribe stays selectable in the dialog.
- **Ayarlar → API Anahtarları** moved into **Tercihler** as a tab.
- CI and release use the `macos-15` runner image; a release is published even when one platform's
  package is missing, so it can be added later.

### Fixed
- GitHub Release notes are taken from this changelog again (they only said "See CHANGELOG.md").

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

- Whisper word timing via DTW alignment, a short punctuated Turkish prompt, and removal of
  non-speech annotations (`*gülüşmeler*`, `[Müzik]`, `(alkış)`, ♪, laughter) before segmentation —
  found necessary on a real phone recording (see `docs/reports/asr-comparison.md`).
- ASR engine comparison report.

### Changed
- **Gemini 3.5 Transcribe is the default engine**; without a stored key the dialog falls back to Whisper.
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

[Unreleased]: https://github.com/eneskaramanoglu/lingocue/compare/v0.8.0...HEAD
[0.8.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/eneskaramanoglu/lingocue/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/eneskaramanoglu/lingocue/releases/tag/v0.1.0
