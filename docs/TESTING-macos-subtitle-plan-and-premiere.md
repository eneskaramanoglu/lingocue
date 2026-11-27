# macOS test: subtitle plan and Premiere overlay (v1.3.0)

Standalone checklist for v1.3.0 on Apple Silicon. The full checklist is [TESTING-macos.md](TESTING-macos.md);
the mixed-language clip used here is made in [TESTING-macos-mixed-languages.md](TESTING-macos-mixed-languages.md)
(section 2).

What v1.3.0 adds:
- **Dile göre altyazı:** a plan that says, per spoken language, what is shown on top and below.
- **Altyazı katmanı:** a transparent MOV of the subtitles for Premiere Pro / DaVinci Resolve.
- **Videoya göm → Biçim:** HEVC 10 bit and ProRes 422 HQ next to H.264.

## 1. Get a build

Before the release (branch `feature/v1.3-subtitle-plan`):

```bash
cd lingocue && git fetch && git checkout feature/v1.3-subtitle-plan
dotnet test --solution LingoCue.slnx
dotnet run --project src/LingoCue.App
```

After the release: install `LingoCue-1.3.0-osx-arm64.dmg` and run
`xattr -dr com.apple.quarantine /Applications/LingoCue.app`. Logs: `~/Library/Logs/LingoCue/`.

## 2. Subtitle plan

Use `mixed.mp4`: transcribe it with Karışık (Türkçe, İngilizce, Korece). Then make an English and a Turkish
translation (Çevir; a paid AI call, a few cents at most).

- [ ] **Card.** Stil → **Dile göre altyazı**. There is one row per spoken language (TR, EN, KO), each with an
  **Üstte** and an **Altta** box.
- [ ] **Plan.** Tick "Konuşulan dile göre farklı altyazı göster" and set:
  - Türkçe: Gösterme / Yok
  - İngilizce: Türkçe çeviri / Yok
  - Korece: Türkçe çeviri / İngilizce çeviri

  In the preview: Turkish speech shows nothing, English speech shows Turkish, and Korean speech shows Turkish
  with English below in the second style (smaller, yellow).
- [ ] **Missing translations.** Delete the English translation track (İzler…).
  - The card says "Korece satırlar için ingilizce çeviri yok (n satır)".
  - **Eksik çevirileri yap** translates only those lines; the message then disappears.
- [ ] **Undo.** ⌘Z undoes a plan change.
- [ ] **Template.** Save the look as a template, open another project, apply it: the plan comes along.

## 3. Exports with the plan

- [ ] **SRT.** Dışa aktar → Altyazı dosyası → SRT.
  - Turkish lines are missing.
  - English lines are in Turkish.
  - Korean lines have two lines: Turkish, then English.
- [ ] **Burn-in, H.264** (VideoToolbox): the video matches the preview.
- [ ] **Burn-in, HEVC 10 bit:**
  - The status names VideoToolbox; the note says x265 only if the Mac cannot encode 10-bit HEVC.
  - `ffprobe` on the output shows `hevc`, `Main 10`, `yuv420p10le`.
- [ ] **Burn-in, ProRes 422 HQ:** a `.mov` that QuickTime plays; `ffprobe` shows `prores`, `HQ`, `yuv422p10le`.

## 4. Overlay for Premiere Pro / Resolve

- [ ] **Animation overlay.** Dışa aktar → **Altyazı katmanı** → QuickTime Animation → `mixed.altyazi-katmani.mov`.
  With a style that fades (e.g. Belgesel) the "Yumuşak geçişleri katmana dahil et" box appears, unticked;
  ticking it makes the file about ten times larger.
- [ ] **ffprobe.** `ffprobe -v error -show_entries stream=codec_name,pix_fmt,width,height,r_frame_rate,duration mixed.altyazi-katmani.mov`
  should show `qtrle` / `argb` at the video's size and frame rate, with the same duration.
- [ ] **Premiere Pro or DaVinci Resolve** (whichever is installed):
  - Import the MOV and put it on V2 over `mixed.mp4` at 00:00.
  - The subtitles appear over the picture with transparent surroundings, and the box keeps its transparency.
  - Note the editor's version. If Animation does not import, try ProRes 4444 and say so.
- [ ] **ProRes 4444 overlay:** the same checks with the ProRes 4444 option.
- [ ] **Timing.** Scrub to the last subtitle: it lines up with the speech, with no drift at the end.

## 5. Report

Post one comment on the macOS test issue with:
- the macOS version, Mac model and RAM;
- the Premiere / Resolve version;
- each box above marked passed or failed;
- the ffprobe lines;
- for failures, the last log lines.

Leave out personal paths and account names.
