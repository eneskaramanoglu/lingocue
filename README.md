# LingoCue

[![CI](https://github.com/eneskaramanoglu/lingocue/actions/workflows/ci.yml/badge.svg)](https://github.com/eneskaramanoglu/lingocue/actions/workflows/ci.yml)

AI-assisted subtitle editor for Windows and macOS (Apple Silicon).

Drop in a video, get word-timed speech recognition (local Whisper or Gemini Transcribe),
readable subtitle lines, an LLM correction pass, live styled preview, translation to English,
and export to SRT / VTT / ASS or a burned-in / soft-subbed MP4.

> **Status:** early development (v0.4.0 = automatic Turkish transcription with local Whisper or Gemini, plus subtitle editing and export). See the [roadmap](docs/PLAN.md#yol-haritası-her-biri-bir-github-milestone-ve-release).

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

Requirements:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- FFmpeg: `winget install Gyan.FFmpeg` (Windows) / `brew install ffmpeg-full` (macOS; the plain `ffmpeg` formula lacks libass, needed to burn in subtitles)
- libmpv: downloaded automatically on Windows builds; on macOS `brew install mpv`

```bash
dotnet build LingoCue.slnx
dotnet test --solution LingoCue.slnx
dotnet run --project src/LingoCue.App
```

## Requirements at runtime

The release packages bundle libmpv. **FFmpeg must be installed separately** (until v1.0):
`winget install Gyan.FFmpeg` on Windows, `brew install ffmpeg-full` on macOS. LingoCue finds it in the
usual winget/Homebrew/Chocolatey/Scoop folders even when it is not on the PATH.

### Transcription

- **Local (Whisper):** free and offline after a one-time model download (large-v3 q5_0 ≈ 1.1 GB,
  turbo ≈ 0.6 GB). Uses an NVIDIA GPU through CUDA when the CUDA Toolkit 13 is installed, otherwise
  Vulkan (any modern GPU driver), otherwise the CPU. On Apple Silicon it uses CoreML.
- **Cloud (Gemini 3.5 Transcribe):** needs a Gemini API key (Google AI Studio); about $0.005 per
  minute of audio. Enter the key in **Ayarlar → API Anahtarları**; it is stored in Windows
  Credential Manager / macOS Keychain, never in files.

## Tech stack

.NET 10 · Avalonia UI · CommunityToolkit.Mvvm · Microsoft.Extensions (DI, Configuration, Logging)
· Serilog · libmpv (HanumanInstitute.LibMpv) · FFmpeg/ffprobe · xUnit v3 + Shouldly.
Whisper.net (local ASR) · Gemini 3.5 Transcribe (cloud ASR). Planned: LLM correction/translation via Microsoft.Extensions.AI.

## Documentation

- [Plan and roadmap](docs/PLAN.md) (Turkish)
- [Architecture](docs/ARCHITECTURE.md)
- [Research notes](docs/RESEARCH.md) (Turkish)
- [macOS manual test checklist](docs/TESTING-macos.md)
- [Architecture decision records](docs/adr/)

## License

[MIT](LICENSE)
