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

## v0.2.0 — Video

Prerequisite: `brew install ffmpeg` (the app does not bundle FFmpeg yet). From source also
`brew install mpv`; the release `.app` bundles libmpv and must work **without** Homebrew mpv.

Test video: any MP4 from your phone (portrait) and one landscape MP4/MOV/MKV. To generate one:
`ffmpeg -f lavfi -i testsrc2=size=1280x720:rate=25:duration=10 -f lavfi -i sine=frequency=440:duration=10 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest test.mp4`

- [ ] Release `.app` launches after `xattr`; if you have Homebrew mpv installed, also test once with
      `brew unlink mpv` (then `brew link mpv`) to prove the bundled libmpv is used.
- [ ] **Video Aç** (and ⌘O) opens the macOS file picker filtered to videos; choosing a file shows the first frame paused.
- [ ] Dragging a video file from Finder onto the window opens it.
- [ ] Dropping a non-video file (e.g. `.txt`) shows a red "Desteklenmeyen dosya türü" banner that **Kapat** dismisses.
- [ ] Status bar shows `name · W×H · fps · codec · N ses izi · duration`; window title shows the file name.
- [ ] Play/pause with the button and with Space; **sound is audible**.
- [ ] ← / → jump 5 s; Shift+← / Shift+→ step one frame (time changes by 1/fps); buttons do the same.
- [ ] Dragging the seek slider moves the video; the time display (`hh:mm:ss.fff / duration`) follows playback.
- [ ] At the end the video stays on the last frame; Play restarts from the beginning.
- [ ] A portrait phone video is shown upright (not rotated) and pillarboxed.
- [ ] Resizing the window and switching Tema Dark/Light keeps the video rendering (no black/frozen view).
- [ ] Subtitle preview: from the source checkout run
      `dotnet run --project src/LingoCue.App -- test.mp4 --subtitle sample.ass` with any ASS file containing
      Turkish text (ğ ş ı İ); the subtitle renders with its style.
- [ ] With FFmpeg uninstalled or unlinked (`brew unlink ffmpeg`), opening a video shows the banner
      "FFmpeg bulunamadı … brew install ffmpeg" (re-link afterwards).
- [ ] Log `~/Library/Logs/LingoCue/` shows "mpv context attached" and "Using ffmpeg at /opt/homebrew/bin/ffmpeg".

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
