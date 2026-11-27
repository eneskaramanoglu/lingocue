# macOS test: mixed-language recognition (v1.2.0)

Standalone checklist for the "Karışık (birden çok dil)" feature on Apple Silicon. It only covers this
feature; the full checklist is [TESTING-macos.md](TESTING-macos.md).

What the feature does: when a video mixes languages (for example Turkish, English and Korean), each
stretch of speech is detected among the chosen languages (at most 3) and written in its own language.
Every subtitle line then carries a language badge (TR, EN, KO).

## 1. Get a build

**Before the release (branch `feature/v1.2-mixed-languages`):**

```bash
git clone https://github.com/eneskaramanoglu/lingocue.git && cd lingocue
git checkout feature/v1.2-mixed-languages
brew install --cask dotnet-sdk          # once; needs .NET 10
brew install ffmpeg-full mpv            # once; plain "ffmpeg" has no libass
dotnet test --solution LingoCue.slnx    # all green
dotnet run --project src/LingoCue.App
```

**After the release:** install `LingoCue-1.2.0-osx-arm64.dmg` from the GitHub release. Then run
`xattr -dr com.apple.quarantine /Applications/LingoCue.app`.

Logs: `~/Library/Logs/LingoCue/`. Lines containing `Mixed languages:` and `Region` show what was detected.

## 2. Make a test clip with the macOS voices

The voices used are Yelda (Turkish), Samantha (English) and Yuna (Korean). If one is missing, add it in
System Settings → Accessibility → Spoken Content → System Voice → Manage Voices. Run this in an empty folder:

```bash
mkdir -p mixed && cd mixed
say -v Yelda    -o 01.aiff "Merhaba arkadaşlar, bugün sizlerle İstanbul'un en güzel semtlerinden birini geziyoruz."
say -v Samantha -o 02.aiff "Hi everyone, today we are exploring one of the most beautiful neighborhoods in Istanbul."
say -v Yelda    -o 03.aiff "Önce Kadıköy çarşısına uğrayacağız ve biraz simit alacağız."
say -v Samantha -o 04.aiff "Okay, let's go."
say -v Yuna     -o 05.aiff "안녕하세요 여러분, 오늘은 서울의 전통 시장을 함께 둘러보겠습니다."
say -v Yelda    -o 06.aiff "Evet."
say -v Samantha -o 07.aiff "Thank you so much for watching, see you in the next video."
say -v Yelda    -o 08.aiff "Görüşmek üzere!"
for f in 0*.aiff; do ffmpeg -loglevel error -y -i "$f" -ar 16000 -ac 1 "${f%.aiff}.wav"; done
ffmpeg -loglevel error -y -f lavfi -i anullsrc=r=16000:cl=mono -t 0.7 pause.wav
: > list.txt; for f in 0*.wav; do echo "file '$PWD/$f'" >> list.txt; echo "file '$PWD/pause.wav'" >> list.txt; done
ffmpeg -loglevel error -y -f concat -safe 0 -i list.txt -c:a pcm_s16le mixed.wav
ffmpeg -loglevel error -y -f lavfi -i color=c=0x1d3557:s=1280x720:r=25 -i mixed.wav -shortest \
  -c:v libx264 -pix_fmt yuv420p -c:a aac mixed.mp4
```

The expected languages in order are `tr,en,tr,en,ko,tr,en,tr`.

## 3. Automatic check (optional, needs the source build)

This runs real Whisper on the clip. The first run downloads the models: large-v3 q5_0 (~1.1 GB) once,
plus the 60 MB language detector.

```bash
LINGOCUE_WHISPER_MIXED_AUDIO="$PWD/mixed.wav" \
LINGOCUE_WHISPER_MIXED_LANGUAGES="tr+en+ko" \
LINGOCUE_WHISPER_MIXED_EXPECTED="tr,en,tr,en,ko,tr,en,tr" \
dotnet test --project tests/LingoCue.Transcription.Tests --filter-method "*Tags_each_stretch*"
cat "$TMPDIR/lingocue-whisper-mixed.txt"
```

- [ ] The test passes. In the report, `Matching runs` is 8/8, or say which lines differ.
- [ ] The report's first line names the device: Apple Metal (GPU), or Neural Engine if it is on.
- [ ] Note the `elapsed` value and the `Mixed languages: … in …` time.

On the Windows reference (GTX 4 GB, Vulkan) a 37-second Turkish/English clip took 21 s mixed and 12 s single-language.

## 4. In the app

- [ ] **Dialog.** Open `mixed.mp4`, then Transkribe et (⌘T) → Konuşma dili → **Karışık (birden çok dil)**.
  - Türkçe is ticked automatically and Başlat stays disabled until a second language is ticked.
  - After three ticks, the other boxes are disabled.
- [ ] **Run.** Tick Türkçe, İngilizce and Korece, then Başlat.
  - The status shows "Dil algılanıyor (n/m)" and then recognition.
  - The track's language is **tr**, and every row has a TR / EN / KO badge.
  - The Korean line is written in Hangul, not translated. "Okay, let's go." stays English and "Evet." stays Turkish.
- [ ] **Badges and filter.**
  - The "Tüm diller" box above the list filters to one language.
  - Select a line, then use the **Dil: TR EN KO** buttons under the editor to change it.
  - ⌘⇧L moves the line to the next language, and ⌘Z undoes it.
- [ ] **Reading rules.** The Korean line's reading-speed chip uses the Korean scale (lower numbers than the same text as Turkish).
- [ ] **Upper case.** In Stil, tick "BÜYÜK HARF (her satır kendi dilinin kurallarıyla …)". English "this is it" must show "THIS IS IT", not "THİS".
- [ ] **Export.** Dışa aktar → Altyazı dosyası (SRT).
  - Tick **Karışık dilli izi dile göre ayrı dosyalara böl** and choose a folder.
  - Expect `mixed.tr.srt`, `mixed.en.srt` and `mixed.ko.srt`.
  - Also try Açılır-kapanır video (MKV): each language should appear as its own subtitle stream in IINA or VLC.
- [ ] **Burn-in.** Export a burned-in video (VideoToolbox). The Korean line must show Hangul glyphs, not boxes.
- [ ] **AI passes (optional, paid; ask the owner first).**
  - Düzelt keeps every line in its own language.
  - Çevir → English translates only the Turkish and Korean lines; the English lines are copied unchanged.

## 5. Report

Send the results as one comment on the macOS test issue. Include:
- the macOS version and the Mac model with its RAM;
- each box above marked passed or failed;
- for anything that failed, the last lines of the log;
- the test report (`lingocue-whisper-mixed.txt`).

Leave out any personal paths or account names.
