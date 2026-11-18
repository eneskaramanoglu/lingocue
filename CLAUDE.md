# CLAUDE.md — LingoCue

AI-assisted video subtitle editor (Windows + macOS). Avalonia UI on .NET 10.
Full plan: [docs/PLAN.md](docs/PLAN.md). Research and rationale: [docs/RESEARCH.md](docs/RESEARCH.md).
Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Decisions: [docs/adr/](docs/adr/).

The owner is an experienced C# backend developer, less experienced in UI: explain UI/XAML
choices briefly in PRs. Conversation language with the owner is Turkish; code, comments,
commit messages and identifiers are English. User-facing UI strings are Turkish.

## Commands

```bash
dotnet build LingoCue.slnx                         # build everything
dotnet test --solution LingoCue.slnx               # run all tests (Microsoft.Testing.Platform runner)
dotnet run --project src/LingoCue.App              # run the app
dotnet publish src/LingoCue.App -c Release -r win-x64 --self-contained -o publish/win-x64
```

- SDK is pinned by `global.json` (10.0.x). `dotnet test` uses the new MTP mode, so pass
  `--solution` / `--project` (not a positional path).
- Logs: Windows `%APPDATA%\LingoCue\logs`, macOS `~/Library/Logs/LingoCue`.
- Run with a file: `dotnet run --project src/LingoCue.App -- video.mp4 [--subtitle preview.ass]`.
- **Native dependencies:** FFmpeg on PATH or a package-manager folder (`winget install Gyan.FFmpeg`,
  `brew install ffmpeg`). libmpv: Windows builds download it automatically (`build/fetch-libmpv.ps1`,
  pinned + SHA256, output in `build/native/`, git-ignored); macOS dev needs `brew install mpv`.
- Media integration tests generate clips with FFmpeg and skip without it; CI sets
  `LINGOCUE_REQUIRE_FFMPEG=1` so they fail instead.
- Opt-in real-engine tests (never in CI): `LINGOCUE_WHISPER_TEST_AUDIO=<turkish audio>` runs Whisper
  (downloads the ~1.1 GB model once to the app-data `models` folder);
  `LINGOCUE_GEMINI_TEST_AUDIO=<turkish audio>` plus a Gemini key (env `GEMINI_API_KEY` or the key saved
  in the app) calls the paid API (~$0.005/min). Both write a readable result to `%TEMP%`/`$TMPDIR`
  (`lingocue-whisper-test.txt`, `lingocue-gemini-test.txt`).
- Test audio in Turkish can be synthesized on Windows with the OneCore "Tolga" voice (WinRT
  `Windows.Media.SpeechSynthesis`); never commit audio/video.

## Layout

| Project | Responsibility | May reference |
| --- | --- | --- |
| `LingoCue.Core` | Domain model, subtitle formats, segmentation, tr-TR text helpers, platform interfaces | nothing |
| `LingoCue.Media` | FFmpeg/ffprobe process wrappers | Core |
| `LingoCue.Transcription` | `ITranscriptionEngine`, Whisper.net, Gemini Transcribe | Core, Media |
| `LingoCue.Llm` | Correction + translation pipeline over `IChatClient` | Core |
| `LingoCue.Infrastructure` | OS-specific implementations (paths, keychain, settings) | Core |
| `LingoCue.App` | Avalonia UI (MVVM), composition root | all |

`tests/LingoCue.Core.Tests/Architecture/LayeringTests.cs` enforces that no library references Avalonia.

## Rules

- **Milestones:** plan first (steps + risks), wait for approval, then work on `feature/mX-...`.
- **Commits:** small, working steps, Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`,
  `build:`, `ci:`, `chore:`, `refactor:`). `dotnet build` and `dotnet test` green before every commit.
- **Before every commit**, scan the staged diff for secrets:
  `git diff --cached | grep -nE "AIza[0-9A-Za-z_-]{20,}|sk-[A-Za-z0-9]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|xox[bp]-|-----BEGIN"`.
  Never commit keys, `.env`, real videos/audio or personal data. The repo is public.
- **Packages:** versions only in `Directory.Packages.props`. Check the package's current docs on
  NuGet/GitHub before using it; API shapes and model names in training data may be stale.
- **Warnings are errors.** Fix analyzers rather than suppressing; suppress only with a comment.
- **Platform code:** only `LingoCue.Infrastructure` may call `OperatingSystem.IsWindows()/IsMacOS()`;
  everything else goes through interfaces in `LingoCue.Core.Platform`. Use `Path.Combine`;
  macOS file systems may be case-sensitive.
- **Turkish text:** casing via `TurkishText` / `CultureInfo("tr-TR")`, never invariant casing.
  Files are UTF-8 (SRT has an optional BOM).
- **Async:** long work never blocks the UI thread; take a `CancellationToken` and report `IProgress<T>`.
- **LLM/ASR:** timestamps come only from the ASR engine; never ask Gemini Flash/Pro for timing.
  Model names are never hard-coded; they come from the provider's model list and settings.
- **Tests:** xUnit v3 + Shouldly. Tests calling real APIs read keys from environment variables
  and are skipped when missing. Media tests generate sample media with FFmpeg at test time.
- **Video:** the UI talks to `IVideoPlayer` only (mpv implementation in `App/Playback`, ADR 0001).
  The mpv view renders through OpenGL on all platforms (ADR 0002); keep `Renderer="OpenGl"`.
- **Preview = output:** the preview ASS comes from `AssScriptBuilder` (Core); burn-in must use the
  same builder. Never render subtitles with Avalonia text for preview.
- **Subtitle text model:** line breaks are `\n`; inline formatting is kept as written (HTML tags from
  SRT/VTT or ASS overrides) and converted by `TagConverter` when writing another format.
- **Turkish typing in UI tests:** this machine has Caps Lock on and a Turkish layout; `SendKeys`
  flips case and turns `i` into `İ`. Paste text via the clipboard instead of typing it.
- **MVVM:** CommunityToolkit.Mvvm with `[ObservableProperty] public partial ...` properties and
  `[RelayCommand]`. Views are resolved from DI; compiled bindings (`x:DataType`) everywhere.
- **Milestone close:** update CHANGELOG, add the version's steps to `docs/TESTING-macos.md`, open a
  PR, merge only with the owner's approval, tag `vX.Y.Z` (release.yml publishes win-x64 + osx-arm64).
