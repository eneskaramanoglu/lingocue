using System.Text;
using System.Text.RegularExpressions;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Formats;

/// <summary>
/// WebVTT (.vtt). Keeps cue identifiers, cue settings and STYLE/REGION blocks; NOTE blocks are dropped.
/// HTML entities are decoded on read and re-encoded on write; a leading <c>&lt;v Name&gt;</c> becomes <see cref="Cue.Speaker"/>.
/// </summary>
public sealed partial class VttFormat : ISubtitleFormat
{
    public string Name => "WebVTT";

    public IReadOnlyList<string> Extensions { get; } = [".vtt"];

    public bool LooksLike(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.TrimStart('﻿').StartsWith("WEBVTT", StringComparison.Ordinal);
    }

    public SubtitleDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var document = new SubtitleDocument();
        var normalized = content.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var blocks = BlankLines().Split(normalized).Where(b => b.Trim().Length > 0).ToList();
        if (blocks.Count == 0)
        {
            return document;
        }

        var start = 0;
        if (blocks[0].StartsWith("WEBVTT", StringComparison.Ordinal))
        {
            var header = blocks[0]["WEBVTT".Length..].Trim();
            document.VttHeader = header.Length > 0 ? header : null;
            start = 1;
        }
        else
        {
            document.Warnings.Add("Missing WEBVTT header.");
        }

        var id = 0;
        var seenCue = false;
        for (var b = start; b < blocks.Count; b++)
        {
            var lines = blocks[b].Split('\n');
            if (lines[0].StartsWith("NOTE", StringComparison.Ordinal))
            {
                continue;
            }

            if (!seenCue && (lines[0].StartsWith("STYLE", StringComparison.Ordinal) || lines[0].StartsWith("REGION", StringComparison.Ordinal)))
            {
                document.VttHeaderBlocks.Add(blocks[b].TrimEnd());
                continue;
            }

            var timingLine = lines[0].Contains("-->", StringComparison.Ordinal) ? 0 : (lines.Length > 1 && lines[1].Contains("-->", StringComparison.Ordinal) ? 1 : -1);
            if (timingLine < 0 || !TryParseTiming(lines[timingLine], out var startTime, out var endTime, out var settings))
            {
                document.Warnings.Add($"Block {b + 1}: not a cue, ignored.");
                continue;
            }

            seenCue = true;
            var text = string.Join('\n', lines.Skip(timingLine + 1));
            var cue = new Cue(++id, startTime, endTime, string.Empty)
            {
                Format = new CueFormatData
                {
                    VttIdentifier = timingLine == 1 ? lines[0].Trim() : null,
                    VttSettings = settings,
                },
            };

            var voice = VoiceTag().Match(text);
            if (voice.Success)
            {
                cue.Speaker = voice.Groups["name"].Value.Trim();
                text = text[voice.Length..].Replace("</v>", string.Empty, StringComparison.Ordinal);
            }

            cue.Text = SubtitleText.NormalizeLines(DecodeEntities(text));
            document.Cues.Add(cue);
        }

        return document;
    }

    public string Write(SubtitleDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sb = new StringBuilder("WEBVTT");
        if (!string.IsNullOrWhiteSpace(document.VttHeader))
        {
            sb.Append(' ').Append(document.VttHeader.Trim());
        }

        sb.Append("\n\n");
        foreach (var block in document.VttHeaderBlocks)
        {
            sb.Append(block.TrimEnd()).Append("\n\n");
        }

        foreach (var cue in document.Cues.OrderBy(c => c.Start).ThenBy(c => c.End))
        {
            if (!string.IsNullOrWhiteSpace(cue.Format.VttIdentifier))
            {
                sb.Append(cue.Format.VttIdentifier).Append('\n');
            }

            sb.Append(SubtitleTime.FormatVtt(cue.Start)).Append(" --> ").Append(SubtitleTime.FormatVtt(cue.End));
            if (!string.IsNullOrWhiteSpace(cue.Format.VttSettings))
            {
                sb.Append(' ').Append(cue.Format.VttSettings);
            }

            sb.Append('\n');
            if (!string.IsNullOrWhiteSpace(cue.Speaker))
            {
                sb.Append("<v ").Append(cue.Speaker).Append('>');
            }

            // Blank lines end a cue in WebVTT; drop them.
            var lines = SubtitleText.NormalizeLines(TagConverter.AssToHtml(cue.Text)).Split('\n').Where(l => l.Length > 0);
            sb.Append(EncodeEntities(string.Join('\n', lines))).Append("\n\n");
        }

        return sb.ToString();
    }

    private static bool TryParseTiming(string line, out TimeSpan start, out TimeSpan end, out string? settings)
    {
        settings = null;
        start = end = TimeSpan.Zero;
        var arrow = line.IndexOf("-->", StringComparison.Ordinal);
        var left = line[..arrow].Trim();
        var right = line[(arrow + 3)..].Trim();
        var space = right.IndexOfAny([' ', '\t']);
        if (space > 0)
        {
            settings = right[(space + 1)..].Trim();
            right = right[..space];
        }

        return SubtitleTime.TryParseSrtOrVtt(left, out start) && SubtitleTime.TryParseSrtOrVtt(right, out end);
    }

    private static string DecodeEntities(string text)
        => text.Replace("&lt;", "<", StringComparison.Ordinal)
               .Replace("&gt;", ">", StringComparison.Ordinal)
               .Replace("&nbsp;", " ", StringComparison.Ordinal)
               .Replace("&lrm;", "‎", StringComparison.Ordinal)
               .Replace("&rlm;", "‏", StringComparison.Ordinal)
               .Replace("&amp;", "&", StringComparison.Ordinal);

    // Escape '&' and any '<' that does not start a formatting tag we keep.
    private static string EncodeEntities(string text)
    {
        var escaped = text.Replace("&", "&amp;", StringComparison.Ordinal);
        return LooseLessThan().Replace(escaped, "&lt;");
    }

    [GeneratedRegex(@"\n[ \t]*\n+")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^<v(?:\.[\w.]+)?\s+(?<name>[^>]+)>")]
    private static partial Regex VoiceTag();

    [GeneratedRegex(@"<(?!/?(?:i|b|u|s|c|v|lang|ruby|rt)(?:[\s.>]))")]
    private static partial Regex LooseLessThan();
}
