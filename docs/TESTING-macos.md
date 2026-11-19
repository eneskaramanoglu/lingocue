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

## v0.5.0 — AI correction

Needs a project with a transcribed (or imported) Turkish track and a Gemini key. A short track
(20–50 lines) keeps the cost near zero; the free tier is enough.

- [ ] **Ayarlar → Tercihler…** (⌘,): four tabs (Genel, Dil modeli (LLM), Altyazı kuralları,
      API anahtarları). On Dil modeli, **Modelleri yenile** says "… model bulundu" and the lists show
      gemini-… text models (no image/TTS models). Change "İstek başına satır", **Kaydet**, reopen: the
      value is kept; `~/Library/Application Support/LingoCue/settings.json` contains it and no key.
- [ ] Altyazı kuralları: lower "En fazla CPS", **Kaydet**: the editor's CPS colours update at once.
- [ ] **Düzelt** (toolbar): the dialog shows line count, model (e.g. gemini-3.8-flash) and a token/cost
      estimate. **Başlat**: status bar shows "Bağlam çıkarılıyor", then batches "n/m"; **İptal** stops
      it and leaves the tracks unchanged.
- [ ] Run to the end: a track "Düzeltilmiş" is added and selected; the status bar lists changed /
      review / failed counts and tokens. Rows show ● (changed), ! (review), ✕ (failed); hovering the
      text shows "Orijinal: …".
- [ ] Select a changed row: "Orijinal: …" appears under the editor; **Orijinale dön** restores it;
      **Kontrol edildi** clears the ! mark. **Değişenler** shows only marked rows.
- [ ] Timing check: start/end times of every row equal the source track's (pick a few rows).
- [ ] **Altyazı → Sözlük ve Bağlam…**: the context (summary, tone, address) is filled; suggested
      names are listed. Lock a term with a spelling (e.g. Ankara'dan), run Düzelt again: the new
      track "Düzeltilmiş 2" uses that spelling.
- [ ] Delete the key in Tercihler → API anahtarları: Düzelt shows the "API anahtarı" instruction and
      **Başlat** is disabled.
- [ ] Save, close, reopen: glossary, context and both tracks are kept.

## v0.4.0 — Transcription

Needs a video with Turkish speech (any phone recording; 1–3 minutes is enough).

- [ ] **Ayarlar → API Anahtarları**: the dialog names "macOS Anahtar Zinciri (Keychain)"; paste the
      Gemini key and **Kaydet** → "Kayıtlı bir anahtar var". Keychain Access.app shows an item
      "LingoCue" with account `gemini-api-key`. Reopen the dialog: the key text is not shown.
- [ ] Open the video, **Transkribe Et** (⌘T): motor Whisper, model large-v3 (q5_0) says
      "İlk kullanımda 1,1 GB indirilecek". **Başlat**: status bar shows "Model indiriliyor %…", then
      "Ses ayıklanıyor", then "Konuşma tanınıyor %… · Apple CoreML" (first run may spend a minute or two
      compiling the CoreML model; later runs are faster). **İptal** stops it.
- [ ] Run again to the end: a track "Kaynak · Whisper large-v3-q5_0" appears with readable rows
      (≤ 2 lines, ≤ 42 chars per line); playing shows the subtitles on the video.
- [ ] Repeat with model large-v3-turbo: noticeably faster; note the time shown in the status bar.
- [ ] **Transkribe Et** again with the same settings: "Projedeki önceki sonucu kullan" is offered and
      creates the track instantly without recognizing again.
- [ ] Motor Gemini 3.5 Transcribe (bulut), "Konuşmacıları ayır" on: finishes with a cost in the status
      bar; with two speakers in the video the rows carry spk_1 / spk_2 in the Konuşmacı column.
- [ ] Delete the key (API Anahtarları → Kayıtlı anahtarı sil): Gemini shows "Gemini API anahtarı
      girilmemiş" with instructions and **Başlat** is disabled.
- [ ] Save, close and reopen the project: the tracks are there and the cached recognition is still offered.
- [ ] Models live in `~/Library/Application Support/LingoCue/models/`.

## v0.3.0 — Subtitle core

Sample SRT (save as `test.srt`, UTF-8):

```
1
00:00:00,500 --> 00:00:02,500
Merhaba! Bugün İstanbul'dayız.

2
00:00:02,800 --> 00:00:04,000
<i>Çok kısa ama çok uzun bir satır, okuma hızı kesinlikle aşılıyor burada</i>

3
00:00:04,500 --> 00:00:07,000
Ilık bir rüzgâr esiyor,
şırıl şırıl akan suyun sesi.
```

- [ ] The window opens maximized; menu **Dosya** is inside the window (not the macOS menu bar yet).
- [ ] Open a video, then **Dosya → Altyazı İçe Aktar…** (⌘I) `test.srt`: the table shows 3 rows,
      status bar says "SubRip, utf-8"; row 2 has a red CPS cell and an underlined text with a tooltip.
- [ ] Press Space: the subtitle appears on the video **in italics for row 2** (not with literal `<i>`),
      Turkish letters (İ ı ğ ş ç ü ö â) correct; the current row is marked and selected while playing.
- [ ] Click row 3: the video jumps to 00:00:04.500 and shows the two-line subtitle.
- [ ] Edit row 3's text in the editor: the table, CPS and the video subtitle update within ~0.5 s;
      the title shows "•".
- [ ] Type `00:00:05.000` into Başlangıç and press Enter: the row moves/updates; typing garbage reverts.
- [ ] ⏱ buttons set start/end to the playhead; **+ Satır** adds a row at the playhead without
      overlapping the cue already shown there; **Sil** deletes the selected row.
- [ ] ⌘S asks for a location the first time and saves `*.lingocue`; the "•" disappears.
      ⌘Q / closing the window with unsaved changes shows Kaydet / Kaydetme / Vazgeç and each works.
- [ ] Reopen the project (Dosya → Proje Aç…, or drag the `.lingocue` file): video, rows and edits return.
- [ ] Move the project file and video together to another folder (or copy them from Windows): it still opens.
- [ ] Export SRT/VTT/ASS from **Dosya → Altyazı Dışa Aktar**; open the SRT in TextEdit (UTF-8, CRLF)
      and the ASS in another player (IINA/VLC): styling and Turkish text look like the preview.
- [ ] Dragging an `.srt` onto the window imports it (asks before replacing existing rows).
- [ ] A Windows-1254 (ANSI Turkish) SRT imports with correct letters.

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
- [ ] (Replaced in v0.3.0 by importing subtitles from the menu.) Subtitle preview: from the source checkout run
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
