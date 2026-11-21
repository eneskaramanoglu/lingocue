# LingoCue

[![CI](https://github.com/eneskaramanoglu/lingocue/actions/workflows/ci.yml/badge.svg)](https://github.com/eneskaramanoglu/lingocue/actions/workflows/ci.yml)

AI-assisted subtitle editor for Windows and macOS (Apple Silicon). The app speaks Turkish.

Drop in a video and get:
- word-timed speech recognition (local Whisper or Gemini Transcribe),
- readable subtitle lines and an AI correction pass (Gemini or a local model through Ollama),
- translation into several languages at once (English, Korean and more),
- a live styled preview with background boxes, karaoke and bilingual layout,
- export to SRT / VTT / ASS, or a burned-in or soft-subbed video.

**User guide (Turkish):** [docs/KULLANIM.md](docs/KULLANIM.md)

## Download

Get the latest version from [Releases](https://github.com/eneskaramanoglu/lingocue/releases/latest).

- **Windows (x64):** `LingoCue-win-Setup.exe`. It installs per user, with no admin rights, and updates
  itself. Windows SmartScreen may warn because the app is not code-signed: **More info → Run anyway**.
  `LingoCue-win-Portable.zip` runs without installing.
- **macOS (Apple Silicon):** `LingoCue-<version>-osx-arm64.dmg`. Drag LingoCue to Applications.

### macOS: the app is not notarized

The macOS build is not signed with an Apple Developer ID, so Gatekeeper reports it as damaged
or blocks it. Remove the quarantine flag once after copying it to Applications:

```bash
xattr -dr com.apple.quarantine /Applications/LingoCue.app
```

The app tells you when a new version is out; install the new `.dmg` the same way.

### FFmpeg is installed separately

The packages bundle libmpv for the preview. **FFmpeg must be installed separately**:
- Windows: `winget install Gyan.FFmpeg`
- macOS: `brew install ffmpeg-full`

LingoCue finds it in the usual winget, Homebrew, Chocolatey and Scoop folders, even when it is not on
the PATH.

## Updates and privacy

- **Update check.** At start-up, at most once a day, the app asks GitHub for the latest version number.
  You can turn this off in Tercihler → Genel.
- **Videos.** They stay on your computer with local Whisper and Ollama.
- **Cloud engines.** Gemini receives the audio (Gemini Transcribe) or the subtitle text (correction and
  translation).
- **API keys.** They are stored in Windows Credential Manager or the macOS Keychain, never in files.
- **Bug reports.** Yardım → Tanılama paketi oluştur saves a zip that you can attach to a bug report;
  nothing is sent automatically.

## Build from source

Requirements:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- FFmpeg: `winget install Gyan.FFmpeg` (Windows) or `brew install ffmpeg-full` (macOS). The plain
  `ffmpeg` formula lacks libass, which burning in subtitles needs.
- libmpv: downloaded automatically on Windows builds; on macOS `brew install mpv`.

```bash
dotnet build LingoCue.slnx
dotnet test --solution LingoCue.slnx
dotnet run --project src/LingoCue.App
```

Packaging: `.github/workflows/release.yml` builds both platforms on a `v*` tag. Windows uses
Velopack (`build/windows/pack.ps1`); macOS uses `build/macos/make-app.sh` and `build/macos/make-dmg.sh`.
`build/appicon/make-icon.cs` regenerates the icons.

## Tech stack

- .NET 10 · Avalonia UI · CommunityToolkit.Mvvm · Microsoft.Extensions · Serilog
- libmpv (HanumanInstitute.LibMpv) · FFmpeg/ffprobe · libass
- Whisper.net (whisper.cpp; CUDA, Vulkan, Metal) · Gemini (Google Gen AI SDK, Microsoft.Extensions.AI) · Ollama
- Velopack · xUnit v3 + Shouldly

## Documentation

- [User guide](docs/KULLANIM.md) (Turkish)
- [Plan and roadmap](docs/PLAN.md) (Turkish)
- [Architecture](docs/ARCHITECTURE.md)
- [Research notes](docs/RESEARCH.md) (Turkish)
- [macOS manual test checklist](docs/TESTING-macos.md)
- [Architecture decision records](docs/adr/)

## License

LingoCue is [MIT](LICENSE) licensed. The Windows package bundles libmpv, which is GPL-2.0-or-later;
its source is at [mpv.io](https://mpv.io). FFmpeg is not bundled. Yardım → LingoCue hakkında lists every
component and its license.
