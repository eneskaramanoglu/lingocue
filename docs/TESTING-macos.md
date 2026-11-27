# macOS manual test checklist

Target: Apple Silicon (osx-arm64). Each release adds its own section; run the newest section and
spot-check older ones.

## Getting a build

**From a release (preferred):**

1. Download `LingoCue-<version>-osx-arm64.dmg` from the GitHub Release (0.12 and older: the `.zip`).
2. Open it and drag `LingoCue` onto `Applications`.
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

## v1.3.0 — Subtitle plan, Premiere overlay, master formats

- [ ] Run the separate checklist [TESTING-macos-subtitle-plan-and-premiere.md](TESTING-macos-subtitle-plan-and-premiere.md):
  the per-language subtitle plan, the transparent overlay in Premiere/Resolve, and HEVC 10 bit / ProRes burn-in.

## v1.2.0 — Mixed languages

- [ ] Run the separate checklist [TESTING-macos-mixed-languages.md](TESTING-macos-mixed-languages.md):
  a Turkish / English / Korean clip made with the macOS voices, the "Karışık" choice, line badges and export by language.

## v1.1.0 — Gallery, rounded box, Neural Engine

- [ ] **Neural Engine.**
  - Tercihler → Genel shows "Neural Engine hızlandırması" (it is hidden on Windows). Turn it on and transcribe a short video.
  - The first run downloads ~1.2 GB and may sit at "Neural Engine hazırlanıyor" for a few minutes.
  - The status then names **Apple Neural Engine (CoreML) + Apple Metal (GPU)**.
  - A second run starts quickly. Compare its time with the option off (it should be faster or similar).
  - Turn the option off: the next run says "Apple Metal (GPU)" again, without downloading anything.
- [ ] **Template gallery.** Open the Stil tab:
  - The cards show the selected line over the video frame.
  - Without a video they show the drawn skyline.
  - A click applies the template and marks the card; Ctrl+Z undoes it.
- [ ] **Rounded box.** Stil → Arka plan kutusu → Blok kutusu, then Köşe yuvarlaklığı 24:
  - The box corners round in the preview and in a burned-in export (VideoToolbox), and the box fits the text with even padding.
  - Try a project font from "Font dosyası ekle".
  - A very long line that wraps keeps a square box.
- [ ] **Doubtful words.** Use a recognized track with a "!" line, for example from a noisy clip:
  - Orange bars appear under the doubtful words on the timeline.
  - The word buttons under the editor play from just before each word.

## v1.0.0 — Packaging, updates, help

Use the release `.dmg` for this section, not a source build. Packaging is what is being tested.
The v0.12.0 section below also still needs a Mac run (Metal, chip detection).

- [ ] The `.dmg` opens a window with `LingoCue` and an `Applications` link; dragging copies the app.
- [ ] Finder and the Dock show the LingoCue icon (blue tile with the subtitle glyph), not a generic app icon.
- [ ] After the `xattr` command the app starts. Without it, macOS says the app is damaged; that is expected
      and is in the user guide.
- [ ] Yardım → LingoCue hakkında shows the right version and the component list.
- [ ] Yardım → Güncellemeleri denetle on the newest version: "En güncel sürümü kullanıyorsunuz".
      (Once a newer release exists: the banner offers "Sürüm sayfasını aç"; it opens the release page in
      the browser.)
- [ ] Tercihler → Genel → Güncellemeler: turn the check off, save, reopen: it stays off.
- [ ] Yardım → Tanılama paketi oluştur: a zip appears on the Desktop and Finder shows it. Inside:
      `ozet.txt` (the hardware line names the Apple chip and its memory),
      `settings.json` without recent files, and `logs/`. No `/Users/<name>` path in any file (`~` instead).
- [ ] Yardım → Log klasörünü aç opens `~/Library/Logs/LingoCue` in Finder.
- [ ] Yardım → Kullanım kılavuzu and Hata bildir open GitHub pages in the browser.
- [ ] Quit and reopen: no update banner appears again on the same day (checked at most once a day).

## v0.12.0 — Background box, hardware, Whisper accuracy

The Mac steps matter most here: Metal and the chip detection could not be tested on Windows.

- [ ] Welcome screen → Hazırlık: "Donanım" shows the chip and memory, for example
      "Apple M3 · 16 GB ortak bellek".
- [ ] Tercihler → Genel: the "Bu bilgisayar" card suggests a Whisper model. With 16 GB it should
      suggest large-v3 (tam) and a ~12B local model. "Önerilen ayarları uygula", then Kaydet: the
      Whisper model and the local batch settings change.
- [ ] **Whisper on the GPU.** Transcribe a short video. While recognizing, the status line names
      the device, and it must be **Apple Metal (GPU)** (or "Apple Neural Engine (CoreML) + Apple
      Metal (GPU)"), not CPU. If it says CPU, send `~/Library/Logs/LingoCue/` (the whisper.cpp
      lines about Metal are logged).
- [ ] Compare the times for "Hızlı" and "En iyi" (Tercihler → Genel → Whisper doğruluğu). "En iyi"
      is expected to take about twice as long.
- [ ] A video with a long pause (5 s or more) followed by speech: the sentence after the pause is in
      the transcript, at the right time.
- [ ] Add a name to the glossary (Sözlük), for example an unusual surname, then transcribe again:
      the name is spelled as in the glossary.
- [ ] Stil → Arka plan kutusu:
  - [ ] Blok kutusu with a frame: one rectangle behind both lines in the preview, with the frame
        around it.
  - [ ] Satır kutusu: one box per line.
  - [ ] Tam genişlik şerit: a band across the whole video.
  - [ ] Change the color's opacity and the paddings: the preview follows at once.
  - [ ] Burn in (VideoToolbox): the video looks the same as the preview.
- [ ] The "Haber şeridi" template applies a dark blue band with a yellow frame.

## v0.11.0 — Korean, several languages, local engine

Needs a project with a corrected Turkish track. A short track keeps Gemini costs near zero.

- [ ] Çevir: select **İngilizce** and **Korece** chips. The estimate says "2 dil". Start: two tracks
      appear, "Çeviri · en" and "Çeviri · ko". The Korean lines are in polite speech (요 endings)
      and names are in Hangul.
- [ ] Select the Korean track: the CPS chips follow the Korean limits. 16 Korean letters per line
      is fine, 17 is flagged. Latin letters count half.
- [ ] Stil → İki dilli mod with the Korean track second: the font is a Korean one (macOS: Apple
      SD Gothic Neo, or Noto Sans KR if installed). Korean shows in the preview and in a
      burned-in export with no boxes. Choose Arial as the second font: a warning appears.
- [ ] Tercihler → Dil modeli → "Ücretsiz katman" on, set 10 per minute, translate: it runs one
      batch at a time without 429 errors.
- [ ] Local engine: `brew install ollama`, run `ollama serve`, then `ollama pull gemma3:12b` (or
      qwen3:8b).
  - [ ] Tercihler → "Bağlan ve listele" shows the model; choose it.
  - [ ] Çevir → Motor "Yerel (Ollama)" shows no cost; the translation finishes.
  - [ ] The welcome screen shows "Yerel model (Ollama) · çalışıyor".
- [ ] Stop Ollama in the middle of a local translation: the run ends with lines marked ✕ (not a
      crash). Start Ollama again, then Altyazı → **Başarısız satırları yeniden dene** fills in
      only those lines.

## v0.10.0 — New look

A visual update only; every feature works as in v0.9.0. Check both themes (Ayarlar → Tema).

- [ ] Start without a file: the welcome screen shows the drop zone, recent files as cards and the
      readiness list. Clicking a card opens it; dropping a video on the window still works.
- [ ] App bar: the menu opens from the bar; the document name gets an orange dot after an edit and
      loses it after ⌘S. Transkribe/Düzelt/Çevir show a green check once that track exists.
      The ⌘ shortcuts still work (⌘Z, ⌘F, ⌘E, ⌘T).
- [ ] Lines list: rows show time range, text and a colored CPS label. ↑/↓ moves the selection,
      the edit card follows, the video seeks. Switching to Stil or Kontrol and back keeps the line.
- [ ] Kontrol: tiles show error/warning/clean counts; the tab header shows the problem count.
- [ ] Dialogs (Transkribe, Düzelt, Çevir, Dışa Aktar, Tercihler): icon header, Vazgeç before the
      main button; Esc closes, Enter starts. Dışa Aktar: the three kind cards select correctly.
- [ ] Ayarlar → Tema → Açık / Koyu / Sistem: every screen stays readable, including the label
      over the video and the timeline.
- [ ] Note: the macOS menu stays inside the window (not the system menu bar) in this version.

## v0.9.0 — Editor polish

Needs a video with speech and a project with lines (any earlier version's project works). On macOS
the shortcuts use ⌘ instead of Ctrl; Alt is ⌥.

- [ ] Open the video: the timeline under the video shows the waveform after a moment, the lines as
      blocks and a red playhead. Close and reopen the same video: the waveform appears at once (cache).
- [ ] Click an empty part of the timeline: the video seeks there. Play: the playhead moves and the
      timeline pages forward. ⌘+scroll zooms around the pointer; scrolling (or a two-finger swipe)
      moves sideways.
- [ ] Drag a block: a dashed outline follows and the video shows the frame now and then; release: the
      line's times change in the list. Drag it onto a neighbour: it stops just before it. Drag a
      block's left or right edge: only the start or end changes.
- [ ] Select a line (in the list, not in a text box): ⌥+→ moves it 100 ms later, ⌥+⇧+→ moves only
      its start. `[` and `]` set start/end to the playhead.
- [ ] ⌘+B in the list splits the line in the middle (or at the spoken word); ⌘+B while the caret is in
      the text box splits at the caret. ⌘+M merges with the next line. ⌘+Enter adds a new line.
- [ ] ⌘+Z undoes each of the above one by one; ⌘+⇧+Z redoes. Typing a sentence and undoing removes
      it in one step. Undo back to the saved state: the title's unsaved mark disappears.
- [ ] **Kontrol** tab: problems are listed with line number and time; clicking one selects the line
      and seeks the video. Make two lines overlap → **Çakışmaları gider** fixes it; make a line very
      short with a long text → **Okuma hızı için süreyi uzat** lengthens it. ⌘+Z undoes each fix.
- [ ] ⌘+F: search "istanbul" finds "İstanbul" (Turkish case rules); Enter / ⇧+Enter step through
      matches; **Tümü** replaces all and one ⌘+Z undoes it. Esc closes the bar.
- [ ] Dosya → Son açılanlar lists the project and the video; picking one opens it. Move the file away
      and pick it: an error says it is missing and the entry disappears.
- [ ] Yardım → Klavye kısayolları opens the list.

## v0.8.0 — Translation and tracks

Needs a project with a transcribed and corrected Turkish track (v0.5.0) and a Gemini key. A short
track keeps the cost near zero.

- [ ] **Çevir**: the source is the "Düzeltilmiş" track; the info line says the original speech is
      added as a reference. Target English; the estimate shows model and tokens. **Başlat**: progress
      in the status bar; a track "Çeviri · en" appears with the same start/end times as Turkish.
- [ ] In the English track the editor shows "Orijinal: <Turkish line>" under the text; rows are not
      all marked as changed; the filter is called "Kontrol bekleyenler". CPS colours are milder than
      for the same speed in Turkish (English allows 20 CPS).
- [ ] A line with "o" (he/she) is translated consistently for the same person.
- [ ] Stil → İki dilli mod: the new English track is preselected as the second track.
- [ ] **İzler…** (button next to the track selector): rename a track, change its language, move it
      up/down, duplicate it, add an empty track. Close: the track list shows the changes.
- [ ] Change a start time in the Turkish track, then İzler → select the English track →
      **Zamanlamayı kaynaktan al**: the English line gets the new time; the message counts the lines.
- [ ] Delete the "Düzeltilmiş" track (confirm): the English track still shows "Orijinal: …" (now the
      recognized text).
- [ ] Import an SRT named `test.en.srt` into a project with lines: choose "Yeni iz olarak ekle"; a new
      track with language `en` appears and the old track is untouched.
- [ ] Save, close, reopen: all tracks, names, languages and order are kept.

## v0.7.0 — Export

Needs a project with a video and at least one track; for soft subtitles ideally two tracks (e.g.
Turkish + an imported English SRT). Use a short video (under a minute) to keep renders quick.

- [ ] **Dışa Aktar** (toolbar or ⌘E) opens the dialog with three choices.
- [ ] **Altyazı dosyası**, SRT, "Premiere Pro uyumlu" on, one track: the save dialog suggests
      `<video>.tr.srt`; the file opens in TextEdit with ğ ş ı İ intact. With two tracks a folder is asked
      and two files appear.
- [ ] ASS: the file opens in IINA/VLC next to the video and looks like the preview.
- [ ] **Videoya göm**: the encoder list shows "Apple donanımı (VideoToolbox)" (Apple Silicon). Start:
      "İşler (1)" appears bottom right; open it: progress, speed (e.g. 4.0x) and time left move;
      the job ends with "Bitti" and **Göster** opens Finder with the file selected.
- [ ] Play the MP4 in QuickTime: subtitles look like the preview (font, colors, position; karaoke
      and bilingual if they are on). Audio is in sync over the whole video.
- [ ] Repeat with "İşlemci (x264)": slower, smaller file, same look.
- [ ] Start a render and **İptal** in İşler: "İptal edildi", and no `.lingocue-partial` file remains
      in the target folder.
- [ ] Use a font added with "Font dosyası ekle" (v0.6.0): the burned-in video uses it.
- [ ] **Açılıp kapanabilen altyazılı video**, MP4, two tracks: QuickTime's subtitle menu lists
      "Türkçe" and "English (…)"; switching works. MKV: open in IINA/VLC, the ASS track is styled.
- [ ] A video recorded on an iPhone in HDR: the dialog warns about HDR/10-bit; the render works.
- [ ] Quit the app during a render: it asks first; choosing "İptal et ve kapat" closes it.

## v0.6.0 — Style

Needs a project with a video and a transcribed Turkish track (word timings come from Transkribe Et);
for bilingual mode also a second track (import any English SRT with **Altyazı içe aktar**).

- [ ] **Stil** (toolbar) opens the Stil tab next to Satırlar; the video stays visible. Switch between
      the tabs several times: no crash, values stay.
- [ ] Change font size, color, outline, shadow, alignment (3×3 tiles) and margins: the preview on the
      video changes immediately. Tick "Arka plan kutusu": a dark box appears behind the text.
- [ ] Font list contains the Mac's fonts (e.g. Helvetica, Avenir, Arial Black). Type a name that does
      not exist: an orange warning says the font is missing. Pick "Apple Color Emoji" or "Zapfino":
      a warning about missing Turkish letters (if the font lacks them).
- [ ] Save the project, **Font dosyası ekle…** and pick a .ttf/.otf that is *not* installed (e.g. a
      downloaded Google Font): the file is copied to `<project folder>/fonts/`, the font is selected,
      and the preview uses it (compare letter shapes with Arial).
- [ ] Templates: apply each built-in one. "Shorts / Reels" turns text to capitals with correct
      İ/I and highlights the spoken word while playing. "Belgesel" shows a dark box.
- [ ] **Şablon olarak kaydet…**, give a Turkish name (e.g. "Şık başlık"); restart the app: it is in
      the list. **Dışa aktar…** to a .json, **Sil**, then **İçe aktar…** the .json: it is back.
      Files live in `~/Library/Application Support/LingoCue/templates/`.
- [ ] Karaoke "Dolan renk": words fill with the highlight color in time with speech. On the
      "Düzeltilmiş" track (from v0.5.0) karaoke still follows speech.
- [ ] İki dilli mod: tick it, choose the English track: English appears under Turkish without
      overlapping, also with two-line cues; switch alignment to top: Turkish on top, English under it.
- [ ] Güvenli alan kılavuzlarını göster: two dashed rectangles over the picture (also for a 9:16 video).
- [ ] Export ASS (Dışa Aktar → ASS) and open it in IINA/VLC: looks like the preview.

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

Prerequisite: `brew install ffmpeg-full` (the app does not bundle FFmpeg yet; the plain `ffmpeg` formula has no libass, so burn-in would be unavailable). From source also
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
      "FFmpeg bulunamadı … brew install ffmpeg-full" (re-link afterwards).
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
