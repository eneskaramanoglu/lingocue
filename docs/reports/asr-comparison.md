# ASR engine comparison (M3)

Date: 2026-11-18 · LingoCue v0.4.0 (feature/m3-transcription) · Windows 11, RTX 3050 Laptop (4 GB), no CUDA Toolkit → Whisper ran on **Vulkan**.

## Sample

18.7 s of Turkish speech synthesized with the Windows OneCore voice "Tolga" (no real recording was
available), with pauses of 1.2–2 s between sentences:

> Merhaba, bugün İstanbul'dayız. ‖ Boğaz'ın kıyısında ılık bir rüzgâr esiyor ve martılar uçuşuyor. ‖
> Bu videoda size şehrin en güzel köşelerini göstereceğim. Hazırsanız başlayalım! ‖ Görüşmek üzere.

**Caveat:** synthetic speech is clean, single-speaker and studio-quiet. It shows that both engines
work end to end, their timing behaviour and cost, but it does **not** measure accuracy on real
recordings (noise, music, accents, overlapping speakers, proper names). Re-run on a real video before
treating the accuracy rows as decisive (procedure at the end).

## Results

| | Whisper large-v3 q5_0 | Whisper large-v3-turbo q5_0 | Gemini 3.5 Transcribe |
| --- | --- | --- | --- |
| Where | local (Vulkan GPU) | local (Vulkan GPU) | Google cloud |
| Wall time, 18.7 s audio | **7.5 s** (incl. model load) | **5.0 s** (incl. model load) | **10.6 s** (upload + recognition) |
| Cost | free | free | **~$0.0016** (≈ $0.005/min) |
| One-time download | 1.08 GB | 0.57 GB | — |
| Words recognized | 24/24 | 24/24 | 24/24 |
| Word timestamp resolution | 10 ms | 10 ms | **100 ms** (all offsets were multiples of 0.1 s) |
| Speakers | — | — | yes (`spk:0`; one speaker here) |
| Language detected | tr | tr | tr (requested `tr-TR`) |

Text produced (all three identical except punctuation):

| Engine | Text |
| --- | --- |
| Whisper large-v3 | Merhaba**,** bugün İstanbul'dayız. Boğazın kıyısında ılık bir rüzgar esiyor ve martılar uçuşuyor. Bu videoda size şehrin en güzel köşelerini göstereceğim. Hazırsanız başlayalım. Görüşmek üzere. |
| Whisper turbo | identical to large-v3 |
| Gemini | Merhaba**.** Bugün İstanbul'dayız. … identical from there on |

Differences against the script, shared by all engines: `Boğaz'ın` → `Boğazın` (missing apostrophe),
`rüzgâr` → `rüzgar` (circumflex), `başlayalım!` → `başlayalım.` These are exactly the kind of fixes the
M4 LLM correction pass is for.

## Resulting cues (after LingoCue segmentation)

| # | Whisper large-v3 | Gemini 3.5 Transcribe |
| --- | --- | --- |
| 1 | 00:00.120 → 00:00.930 `Merhaba, bugün` | 00:00.100 → 00:02.100 `Merhaba. Bugün İstanbul'dayız.` |
| 2 | 00:01.010 → 00:02.090 `İstanbul'dayız.` | 00:04.400 → 00:08.038 `Boğazın kıyısında ılık bir rüzgar esiyor` / `ve martılar uçuşuyor.` |
| 3 | 00:04.320 → 00:07.958 `Boğazın kıyısında ılık bir rüzgar esiyor` / `ve martılar uçuşuyor.` | 00:09.500 → 00:12.785 `Bu videoda size` / `şehrin en güzel köşelerini göstereceğim.` |
| 4 | 00:09.460 → 00:12.745 `Bu videoda size` / `şehrin en güzel köşelerini göstereceğim.` | 00:13.100 → 00:14.444 `Hazırsanız başlayalım.` |
| 5 | 00:13.160 → 00:14.504 `Hazırsanız başlayalım.` | 00:17.100 → 00:18.032 `Görüşmek üzere.` |
| 6 | 00:17.060 → 00:17.992 `Görüşmek üzere.` | — |

Whisper's finer word timing exposed a ~450 ms pause after "bugün" (the voice breathes there), so the
first sentence became two cues; Gemini's 100 ms grid hid it. Both splits are valid; Whisper's start
times are 40–80 ms closer to the actual onsets in this sample.

## Assessment

- **Accuracy:** a tie on this clean sample. Real-world difference is unknown until tested on real footage.
- **Timing:** Whisper is finer (10 ms vs 100 ms). This matters for tight cue starts and especially for
  word-by-word karaoke (M5), where 100 ms steps are visible.
- **Speed:** both are fast here. Whisper turbo is fastest; on longer videos Whisper on Vulkan scales
  with GPU speed, Gemini mostly with upload size. CUDA Toolkit 13 would speed Whisper up further.
- **Cost / privacy:** Whisper is free and the audio never leaves the computer; Gemini costs about
  $0.30 per hour of audio and uploads the audio (files are deleted right after).
- **Unique strengths:** Gemini separates speakers (needed for interviews); Whisper works offline.

**Proposed default (sample only, superseded below):** Whisper large-v3 q5_0 for everyday use; Gemini when the video has several
speakers or Whisper struggles with a recording. The dialog remembers the last engine used in a session.

## Real recording (owner's phone video)

39 s handheld phone video, 1280×720, AAC stereo: several people talking casually outdoors, laughter,
interruptions, at least three speakers. The transcript content is private and not reproduced here;
only measurements and observed behaviour are recorded.

| | Whisper large-v3 q5_0 | Whisper turbo q5_0 | Gemini 3.5 Transcribe |
| --- | --- | --- | --- |
| Wall time | 11.2 s | 7.2 s | 13.4 s |
| Cost | free | free | ~$0.0033 |
| Words / cues | 46 words → 9 cues | 66 words → 20 cues | 61 words → 18 cues |
| Punctuation | almost none | sentence ends only | commas and sentence ends |
| Recognition (owner's judgement pending) | several phrases wrong or missing | close to Gemini, a few wrong words | best; reads naturally |
| Non-speech | `*gülüşmeler*` emitted as text | laughter emitted as "Eheheh" words (7×) | none |
| Word timing | **unreliable**: many words snapped to segment edges; cues of 5–7 s | **unreliable**: times snapped to whole seconds (x.000 / x.040) | plausible, 100 ms grid |
| Speakers | — | — | 3 speakers separated |

### What this shows

- On real, noisy, multi-speaker footage **Gemini is clearly better**: text, punctuation, speakers and
  timing all usable as-is.
- **Whisper's token timestamps are not good enough for subtitles here.** whisper.cpp's default token
  times collapse to segment boundaries in fast, overlapping speech. The fix is DTW alignment
  (`WhisperFactoryOptions.UseDtwTimeStamps` with the large-v3 / turbo heads preset), which aligns each
  token against the audio.
- Whisper also needs (a) a Turkish initial prompt with punctuation to bias it toward punctuated output
  and (b) filtering of non-speech annotations (`*…*`, `[…]`, `(…)`, laughter tokens) before segmentation.
- The clean synthetic sample above hid all of this — real footage is the only meaningful test.

### After the Whisper fixes (DTW timing, Turkish punctuation prompt, non-speech filter)

Same video, same machine:

| | Whisper large-v3 q5_0 | Whisper turbo q5_0 | Gemini (unchanged) |
| --- | --- | --- | --- |
| Wall time | 14.6 s | 6.8 s | 13.4 s |
| Words / cues | 55 → 16 | 54 → 14 | 61 → 18 |
| Punctuation | commas, questions, sentence ends | commas, questions, sentence ends | commas, sentence ends |
| Non-speech in text | none | none | none |
| Word timing | plausible; cue starts within ~0.1–0.2 s of Gemini's | plausible; within ~0.1–0.3 s of Gemini's | 100 ms grid |
| Recognition | one phrase still misheard | close to Gemini | best |
| Missed | the last ~4 s (laughter-heavy) | the last ~6 s (laughter-heavy) | nothing |

Whisper is now a usable free/offline alternative; turbo is both the fastest and, on this clip, the more
accurate Whisper variant. On the clean sample the fixes also merged "Merhaba, bugün İstanbul'dayız."
into one cue, because DTW no longer reports a false pause after "bugün".

**Decision (owner, 2026-11-18):** Gemini 3.5 Transcribe is the default engine; Whisper stays as the
free, offline option (the dialog falls back to it when no Gemini key is stored).

## Re-running on a real video

```bash
set LINGOCUE_WHISPER_TEST_AUDIO=<path to a Turkish video>
set LINGOCUE_WHISPER_TEST_MODEL=large-v3-q5_0
dotnet test --project tests/LingoCue.Transcription.Tests -- --filter-class "*WhisperNetEngineIntegrationTests"
set LINGOCUE_GEMINI_TEST_AUDIO=<same path>
dotnet test --project tests/LingoCue.Transcription.Tests -- --filter-class "*GeminiTranscribeLiveTests"
```

Results are written to `%TEMP%\lingocue-whisper-test.txt` and `%TEMP%\lingocue-gemini-test.txt`.
(The assertions expect words from the sample sentence; a real video will fail those checks but still
write the result files.)
