# macOS manual test checklist

Target: Apple Silicon (osx-arm64). Each release adds its own section; run the newest section and
spot-check older ones.

## Getting a build

**From a release (preferred):**

1. Download `LingoCue-<version>-osx-arm64.zip` from the GitHub Release.
2. Unzip (double-click), move `LingoCue.app` to `/Applications`.
3. Remove the quarantine flag: `xattr -dr com.apple.quarantine /Applications/LingoCue.app`

**From source (any branch):**

```bash
git clone https://github.com/eneskaramanoglu/lingocue.git && cd lingocue
git checkout <branch-or-tag>
brew install --cask dotnet-sdk   # once; needs .NET 10
dotnet test --solution LingoCue.slnx
dotnet run --project src/LingoCue.App
```

Logs are written to `~/Library/Logs/LingoCue/`.

## v0.1.0 — Skeleton

- [ ] Release zip unzips to `LingoCue.app`; after `xattr`, double-click opens it without a Gatekeeper error.
- [ ] Window title is "LingoCue"; window is centered and resizable; minimum size is enforced.
- [ ] Toolbar shows Video Aç, Transkribe Et, Düzelt, Çevir, Stil, Dışa Aktar (all disabled).
- [ ] Left: black video placeholder above a timeline placeholder; right: subtitle-lines placeholder.
- [ ] Both splitters (left/right and video/timeline) can be dragged.
- [ ] Status bar shows "Hazır", `osx-arm64` and `v0.1.0`.
- [ ] Tema → Dark / Light switches immediately; System follows macOS Appearance (toggle it in System Settings).
- [ ] Turkish characters render correctly in the UI (Ç, ğ, ı, İ, ş in toolbar labels).
- [ ] Quit with ⌘Q; `~/Library/Logs/LingoCue/lingocue-<date>.log` contains "starting on osx-arm64" and "stopped".
- [ ] From source: `dotnet test --solution LingoCue.slnx` is green.
