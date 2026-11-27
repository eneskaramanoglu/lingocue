# 0004. Subtitles for Premiere Pro: a transparent overlay instead of re-encoding the picture

- Status: accepted
- Date: 2026-11-27

## Context

The owner edits 4K videos of 30–40 minutes in Premiere Pro and wants LingoCue's subtitles on them
without losing quality. Burn-in re-encodes every frame. Until v1.2 it always produced H.264, 8 bit 4:2:0,
which loses 10-bit and HDR sources' depth and adds a generation of compression to a master. SRT import
keeps the picture, but Premiere loses LingoCue's look (boxes, two-line layouts, per-language plans).

## Options considered

1. **Only SRT for Premiere.** No picture loss, but the styling is lost.
2. **A better burn-in only** (HEVC 10 bit, ProRes 422 HQ). Near-lossless, but still one more encode.
   ProRes 422 HQ in 4K is ~2.6 GB per minute (measured).
3. **A transparent subtitle video** rendered with the same ASS script, placed on V2 in the editor.
   The picture is never touched, and Premiere makes its single final encode.

## Decision

Option 3 is the recommended path, with option 2 kept for people who want a finished file out of LingoCue.

- **Overlay:**
  - FFmpeg `color=black@0` canvas, then the `ass` filter, written to MOV.
  - Default codec: QuickTime Animation (`qtrle`, lossless RLE, a key frame every 5 s). ProRes 4444 is
    offered for editors that do not read Animation.
  - Measured in 4K at 29.97 fps with a subtitle every 3 s: Animation took 220 s for 10 minutes (443 MB);
    ProRes 4444 took 105 s per minute (600 MB per minute).
- **Two FFmpeg traps:**
  - Without `alpha=1` the `ass` filter leaves the alpha channel at 0, so the overlay is invisible.
  - With no `YCbCr Matrix` header the filter draws TV-range colors on an RGB canvas (white becomes 235).
    Overlay scripts get `YCbCr Matrix: None`.
  - Burn-in and the preview are left as they were. Changing the matrix there would change colors users
    already approved.
- **Exact frame rate:** the overlay uses ffprobe's nominal rate string (`30000/1001`), not a rounded
  double, so it stays frame-accurate over 40 minutes.
- **Burn-in formats:**
  - HEVC Main10 (hardware only when a test encode of 10-bit HEVC succeeds, otherwise x265) and ProRes 422 HQ.
  - Both force a 10-bit pixel format before the `ass` filter and copy the source's color tags.

## Consequences

- Premiere users keep the picture untouched; correcting a subtitle means re-rendering only the overlay.
- Animation support in the owner's Premiere version still needs a hand check. ProRes 4444 is the fallback.
- ProRes 4444 overlays are large; the dialog says so.
