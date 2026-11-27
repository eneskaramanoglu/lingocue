# 0003. Mixed-language recognition: per-region detection, one decoding pass per language

- Status: accepted
- Date: 2026-11-27

## Context

Videos often mix languages: a Turkish vlog with English lines, an interview half in Korean. whisper.cpp
detects the language once, from the first 30-second window, and decodes the whole recording in that language.
In a mixed video Whisper then translates or garbles every other language. The owner asked for Turkish,
English and Korean to be recognized each in its own language.

Whisper.net 1.9.1 exposes `DetectLanguageWithProbability(samples, candidates)`, which restricts detection to
a candidate list. The Silero VAD regions we already compute (for the packed `SpeechTimeline`) are natural units.

## Options considered

1. **Let Whisper auto-detect per 30-second window.** whisper.cpp does not re-detect after the first window,
   and windows cut through sentences.
2. **Detect each VAD region with the recognition model (large-v3).** Measured on a 37 s Turkish/English clip
   (GTX 4 GB, Vulkan): every region right, short ones included (p ≥ 0.99), but about 2.8 s per region, since
   every call runs the full encoder on a padded 30 s window. Detection took 31 s for 11 regions.
3. **Detect each region with a small model (whisper base q5_1, 60 MB); large-v3 rechecks regions where the
   small model is below p = 0.8.** Same clip: every region right, detection 8 s (2 regions rechecked).
4. **Per-word language from a cloud model.** Gemini's transcription API takes several language codes but
   reports no language per word.

## Decision

Option 3 for Whisper:

- The user picks up to three languages ("Karışık"). The selection is one string (`tr+en+ko`), so settings,
  requests and cached runs compare as before.
- Each VAD region is detected among the chosen languages by the small model. Large-v3 rechecks only the
  unsure ones.
- `LanguageRuns.Smooth` lets a region follow its neighbours only when its guess is unsure (p < 0.8) or both
  short (< 1.5 s) and weak (p < 0.5). A first version also overrode sure short guesses ("Okay, let's go.",
  "Evet."), and Whisper then translated them.
- Regions are grouped by language. Each language is decoded in one pass over its own regions (packed with
  `SpeechTimeline`, its own prompt), and its words are tagged with the language.
- A decoded segment whose text reads as another chosen language (`TextLanguageGuess`, script plus common
  words) is flagged for review. It catches a wrong detection that Whisper then translated.

For Gemini, all chosen languages are sent and sentences are tagged by `TextLanguageGuess`.

The data model gets an optional `Word.Language` and `Cue.Language`. Null means the track's language, so old
projects and one-language tracks are unchanged. The segmenter breaks lines at a language switch, and line
checks, casing, LLM passes and export use the line's own language.

## Consequences

- Mixed recognition costs one small-model encoder pass per region, plus a large-v3 pass for each unsure one.
  On the test clip the total was 21 s, against 12 s for single-language recognition (which got the English
  lines wrong).
- A switch inside one sentence is not split. Whisper writes the sentence in its region's language.
- One more model download (60 MB, pinned with SHA-256 like the others), only when a mixed run is started.
- The language buttons let a person fix a wrong line in one click. Corrections and translations then follow
  the fixed language.
