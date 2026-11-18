# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/eneskaramanoglu/lingocue/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/eneskaramanoglu/lingocue/releases/tag/v0.1.0
