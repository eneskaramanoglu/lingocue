using System.Globalization;
using System.Text;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Formats;

/// <summary>
/// SubRip (.srt). The reader is lenient: missing or wrong index numbers, extra blank lines,
/// <c>.</c> instead of <c>,</c>, cue text containing blank lines and trailing garbage are tolerated.
/// HTML-like tags (<c>&lt;i&gt;</c>, <c>&lt;b&gt;</c>, <c>&lt;font&gt;</c>) are kept verbatim.
/// </summary>
public sealed class SrtFormat : ISubtitleFormat
{
    public string Name => "SubRip";

    public IReadOnlyList<string> Extensions { get; } = [".srt"];

    public bool LooksLike(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        foreach (var line in content.AsSpan().EnumerateLines())
        {
            if (TryParseTiming(line, out _, out _))
            {
                return !content.TrimStart('﻿').StartsWith("WEBVTT", StringComparison.Ordinal);
            }
        }

        return false;
    }

    public SubtitleDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var document = new SubtitleDocument();
        var lines = content.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        Cue? current = null;
        var text = new List<string>();
        var id = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (TryParseTiming(lines[i], out var start, out var end))
            {
                Flush(beforeNextCue: true);
                if (end < start)
                {
                    document.Warnings.Add($"Line {i + 1}: end time is before start time; swapped.");
                    (start, end) = (end, start);
                }

                current = new Cue(++id, start, end, string.Empty);
                continue;
            }

            if (current is not null)
            {
                text.Add(lines[i]);
            }
            else if (!string.IsNullOrWhiteSpace(lines[i]) && !IsIndexLine(lines[i]))
            {
                document.Warnings.Add($"Line {i + 1}: text outside of a cue ignored.");
            }
        }

        Flush(beforeNextCue: false);
        return document;

        void Flush(bool beforeNextCue)
        {
            if (current is null)
            {
                return;
            }

            // The next cue's index number sits right before its timing line. Only drop it when it is
            // separated by a blank line or is exactly the expected next index, so text like "2024" survives.
            TrimTrailingBlank(text);
            if (beforeNextCue && text.Count > 0 && IsIndexLine(text[^1]) &&
                (text.Count == 1 || string.IsNullOrWhiteSpace(text[^2]) || text[^1].Trim() == (id + 1).ToString(CultureInfo.InvariantCulture)))
            {
                text.RemoveAt(text.Count - 1);
                TrimTrailingBlank(text);
            }

            current.Text = SubtitleText.NormalizeLines(string.Join('\n', text));
            document.Cues.Add(current);
            current = null;
            text.Clear();
        }
    }

    public string Write(SubtitleDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sb = new StringBuilder();
        var index = 1;
        foreach (var cue in document.Cues.OrderBy(c => c.Start).ThenBy(c => c.End))
        {
            sb.Append(index++.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(SubtitleTime.FormatSrt(cue.Start)).Append(" --> ").Append(SubtitleTime.FormatSrt(cue.End)).Append('\n');

            // A blank line inside the text would end the cue for most readers.
            var safeText = string.Join('\n', SubtitleText.NormalizeLines(cue.Text).Split('\n').Where(l => l.Length > 0));
            sb.Append(safeText).Append('\n').Append('\n');
        }

        return sb.ToString();
    }

    internal static bool TryParseTiming(ReadOnlySpan<char> line, out TimeSpan start, out TimeSpan end)
    {
        start = end = TimeSpan.Zero;
        var arrow = line.IndexOf("-->", StringComparison.Ordinal);
        if (arrow < 0)
        {
            return false;
        }

        var left = line[..arrow];
        var right = line[(arrow + 3)..].Trim();

        // Ignore trailing SRT coordinates ("X1:...") or VTT-style settings after the end time.
        var space = right.IndexOfAny(' ', '\t');
        if (space > 0)
        {
            right = right[..space];
        }

        return SubtitleTime.TryParseSrtOrVtt(left, out start) && SubtitleTime.TryParseSrtOrVtt(right, out end);
    }

    private static bool IsIndexLine(string line)
        => line.Trim().Length > 0 && line.Trim().All(char.IsAsciiDigit);

    private static void TrimTrailingBlank(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }
}
