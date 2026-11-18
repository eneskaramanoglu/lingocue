# LingoCue

[![CI](https://github.com/eneskaramanoglu/lingocue/actions/workflows/ci.yml/badge.svg)](https://github.com/eneskaramanoglu/lingocue/actions/workflows/ci.yml)

AI-assisted subtitle editor for Windows and macOS (Apple Silicon).

Drop in a video, get word-timed speech recognition (local Whisper or Gemini Transcribe),
readable subtitle lines, an LLM correction pass, live styled preview, translation to English,
and export to SRT / VTT / ASS or a burned-in / soft-subbed MP4.

> **Status:** early development (v0.1.0 = app skeleton). See the [roadmap](docs/PLAN.md#yol-haritası-her-biri-bir-github-milestone-ve-release).

## Download

Grab the latest build from [Releases](https://github.com/eneskaramanoglu/lingocue/releases):

- **Windows (x64):** `LingoCue-<version>-win-x64.zip` → unzip → run `LingoCue.exe`.
- **macOS (Apple Silicon):** `LingoCue-<version>-osx-arm64.zip` → unzip → move `LingoCue.app` to
  `/Applications`.

### macOS: the app is not notarized

The macOS build is not signed with an Apple Developer ID, so Gatekeeper reports it as damaged
or blocks it. Remove the quarantine flag once after unzipping:

```bash
xattr -dr com.apple.quarantine /Applications/LingoCue.app
```

## Build from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). FFmpeg will be
needed from v0.2.0 on (`winget install Gyan.FFmpeg` / `brew install ffmpeg`).

```bash
dotnet build LingoCue.slnx
dotnet test --solution LingoCue.slnx
dotnet run --project src/LingoCue.App
```

## Tech stack

.NET 10 · Avalonia UI · CommunityToolkit.Mvvm · Microsoft.Extensions (DI, Configuration, Logging)
· Serilog · xUnit v3 + Shouldly. Planned: libmpv (preview), FFmpeg, Whisper.net, Google Gemini via
Microsoft.Extensions.AI.

## Documentation

- [Plan and roadmap](docs/PLAN.md) (Turkish)
- [Architecture](docs/ARCHITECTURE.md)
- [Research notes](docs/RESEARCH.md) (Turkish)
- [macOS manual test checklist](docs/TESTING-macos.md)
- [Architecture decision records](docs/adr/)

## License

[MIT](LICENSE)
