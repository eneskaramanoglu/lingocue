using System.Globalization;
using System.Text;
using LingoCue.Core.Subtitles;

namespace LingoCue.Core.Formats;

/// <summary>
/// Advanced SubStation Alpha (.ass; .ssa v4 styles are read too). Reads/writes <c>[Script Info]</c>,
/// <c>[V4+ Styles]</c> and <c>Dialogue</c> events, honoring each section's <c>Format:</c> line.
/// <c>\N</c> ↔ line break; the <c>Name</c> field ↔ <see cref="Cue.Speaker"/>; override blocks stay in the text.
/// Times have centisecond precision (see <see cref="SubtitleTime.FormatAss"/>).
/// </summary>
public sealed class AssFormat : ISubtitleFormat
{
    private static readonly string[] s_styleFields =
    [
        "Name", "Fontname", "Fontsize", "PrimaryColour", "SecondaryColour", "OutlineColour", "BackColour",
        "Bold", "Italic", "Underline", "StrikeOut", "ScaleX", "ScaleY", "Spacing", "Angle",
        "BorderStyle", "Outline", "Shadow", "Alignment", "MarginL", "MarginR", "MarginV", "Encoding",
    ];

    private static readonly string[] s_eventFields =
        ["Layer", "Start", "End", "Style", "Name", "MarginL", "MarginR", "MarginV", "Effect", "Text"];

    private static readonly HashSet<string> s_knownInfoKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ScriptType", "Title", "PlayResX", "PlayResY", "WrapStyle", "ScaledBorderAndShadow",
    };

    public string Name => "Advanced SubStation Alpha";

    public IReadOnlyList<string> Extensions { get; } = [".ass", ".ssa"];

    public bool LooksLike(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.TrimStart('﻿').TrimStart().StartsWith("[Script Info]", StringComparison.OrdinalIgnoreCase);
    }

    public SubtitleDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var document = new SubtitleDocument();
        var info = new AssScriptInfo();
        var extra = new List<KeyValuePair<string, string>>();
        string[]? styleFormat = null;
        string[]? eventFormat = null;
        var section = string.Empty;
        var id = 0;
        var lineNo = 0;

        foreach (var raw in content.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("!:", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToUpperInvariant();
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].TrimStart();

            switch (section)
            {
                case "SCRIPT INFO":
                    info = ApplyInfo(info, key, value, extra);
                    break;

                case "V4+ STYLES" or "V4 STYLES":
                    if (key.Equals("Format", StringComparison.OrdinalIgnoreCase))
                    {
                        styleFormat = SplitFormat(value);
                    }
                    else if (key.Equals("Style", StringComparison.OrdinalIgnoreCase))
                    {
                        var style = ParseStyle(value, styleFormat ?? s_styleFields, section == "V4 STYLES");
                        if (style is null)
                        {
                            document.Warnings.Add($"Line {lineNo}: unreadable style.");
                        }
                        else
                        {
                            document.Styles.Add(style);
                        }
                    }

                    break;

                case "EVENTS":
                    if (key.Equals("Format", StringComparison.OrdinalIgnoreCase))
                    {
                        eventFormat = SplitFormat(value);
                    }
                    else if (key.Equals("Dialogue", StringComparison.OrdinalIgnoreCase))
                    {
                        var cue = ParseDialogue(value, eventFormat ?? s_eventFields, ++id);
                        if (cue is null)
                        {
                            document.Warnings.Add($"Line {lineNo}: unreadable dialogue.");
                            id--;
                        }
                        else
                        {
                            document.Cues.Add(cue);
                        }
                    }

                    break;
            }
        }

        document.ScriptInfo = info with { Extra = extra };
        return document;
    }

    public string Write(SubtitleDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var info = document.ScriptInfo ?? new AssScriptInfo();
        var styles = document.Styles.Count > 0 ? document.Styles : [new SubtitleStyle()];
        var sb = new StringBuilder();

        sb.Append("[Script Info]\n");
        sb.Append("; Script generated by LingoCue\n");
        sb.Append("ScriptType: v4.00+\n");
        if (!string.IsNullOrWhiteSpace(info.Title))
        {
            sb.Append("Title: ").Append(info.Title).Append('\n');
        }

        sb.Append(Inv($"PlayResX: {info.PlayResX}\n"));
        sb.Append(Inv($"PlayResY: {info.PlayResY}\n"));
        sb.Append(Inv($"WrapStyle: {info.WrapStyle}\n"));
        sb.Append("ScaledBorderAndShadow: ").Append(info.ScaledBorderAndShadow ? "yes" : "no").Append('\n');
        foreach (var (key, value) in info.Extra)
        {
            sb.Append(key).Append(": ").Append(value).Append('\n');
        }

        sb.Append("\n[V4+ Styles]\n");
        sb.Append("Format: ").Append(string.Join(", ", s_styleFields)).Append('\n');
        foreach (var style in styles)
        {
            sb.Append("Style: ").Append(FormatStyle(style)).Append('\n');
        }

        sb.Append("\n[Events]\n");
        sb.Append("Format: ").Append(string.Join(", ", s_eventFields)).Append('\n');
        var defaultStyle = styles[0].Name;
        foreach (var cue in document.Cues.OrderBy(c => c.Start).ThenBy(c => c.End))
        {
            var f = cue.Format;
            sb.Append(Inv($"Dialogue: {f.Layer},{SubtitleTime.FormatAss(cue.Start)},{SubtitleTime.FormatAss(cue.End)},"));
            sb.Append(f.Style ?? defaultStyle).Append(',');
            sb.Append(Sanitize(cue.Speaker)).Append(',');
            sb.Append(Inv($"{f.MarginL},{f.MarginR},{f.MarginV},"));
            sb.Append(Sanitize(f.Effect)).Append(',');
            sb.Append(ToAssText(cue.Text)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Model text → ASS event text: HTML tags become overrides, line breaks become <c>\N</c>.</summary>
    public static string ToAssText(string text)
        => TagConverter.HtmlToAss(SubtitleText.NormalizeLines(text)).Replace("\n", "\\N", StringComparison.Ordinal);

    private static AssScriptInfo ApplyInfo(AssScriptInfo info, string key, string value, List<KeyValuePair<string, string>> extra)
    {
        switch (key.ToUpperInvariant())
        {
            case "TITLE":
                return info with { Title = value };
            case "PLAYRESX" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x):
                return info with { PlayResX = x };
            case "PLAYRESY" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y):
                return info with { PlayResY = y };
            case "WRAPSTYLE" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w):
                return info with { WrapStyle = w };
            case "SCALEDBORDERANDSHADOW":
                return info with { ScaledBorderAndShadow = value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) };
            default:
                if (!s_knownInfoKeys.Contains(key))
                {
                    extra.Add(new(key, value));
                }

                return info;
        }
    }

    private static SubtitleStyle? ParseStyle(string value, string[] format, bool legacySsa)
    {
        var parts = value.Split(',', format.Length);
        if (parts.Length < format.Length)
        {
            return null;
        }

        var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < format.Length; i++)
        {
            f[format[i]] = parts[i].Trim();
        }

        try
        {
            var alignment = Int(f, "Alignment", 2);
            if (legacySsa)
            {
                // SSA v4 alignment: 1-3 bottom, +4 top (5-7), +8 middle (9-11).
                alignment = alignment switch { >= 9 => alignment - 5, >= 5 => alignment + 2, _ => alignment };
            }

            return new SubtitleStyle
            {
                Name = Str(f, "Name", "Default"),
                FontName = Str(f, "Fontname", "Arial"),
                FontSize = Dbl(f, "Fontsize", 48),
                PrimaryColor = Color(f, "PrimaryColour", AssColor.White),
                SecondaryColor = Color(f, "SecondaryColour", AssColor.Yellow),
                OutlineColor = Color(f, "OutlineColour", f.TryGetValue("TertiaryColour", out var t) ? AssColor.ParseAss(t) : AssColor.Black),
                BackColor = Color(f, "BackColour", new AssColor(0, 0, 0, 128)),
                Bold = Bool(f, "Bold"),
                Italic = Bool(f, "Italic"),
                Underline = Bool(f, "Underline"),
                StrikeOut = Bool(f, "StrikeOut"),
                ScaleX = Dbl(f, "ScaleX", 100),
                ScaleY = Dbl(f, "ScaleY", 100),
                Spacing = Dbl(f, "Spacing", 0),
                Angle = Dbl(f, "Angle", 0),
                BorderStyle = Int(f, "BorderStyle", 1) == 3 ? AssBorderStyle.OpaqueBox : AssBorderStyle.Outline,
                Outline = Dbl(f, "Outline", 2),
                Shadow = Dbl(f, "Shadow", 0),
                Alignment = alignment,
                MarginL = Int(f, "MarginL", 10),
                MarginR = Int(f, "MarginR", 10),
                MarginV = Int(f, "MarginV", 10),
                Encoding = Int(f, "Encoding", 1),
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string FormatStyle(SubtitleStyle s) => string.Join(',',
        Sanitize(s.Name), Sanitize(s.FontName), Num(s.FontSize),
        s.PrimaryColor.ToAssStyle(), s.SecondaryColor.ToAssStyle(), s.OutlineColor.ToAssStyle(), s.BackColor.ToAssStyle(),
        s.Bold ? "-1" : "0", s.Italic ? "-1" : "0", s.Underline ? "-1" : "0", s.StrikeOut ? "-1" : "0",
        Num(s.ScaleX), Num(s.ScaleY), Num(s.Spacing), Num(s.Angle),
        ((int)s.BorderStyle).ToString(CultureInfo.InvariantCulture), Num(s.Outline), Num(s.Shadow),
        s.Alignment.ToString(CultureInfo.InvariantCulture),
        s.MarginL.ToString(CultureInfo.InvariantCulture), s.MarginR.ToString(CultureInfo.InvariantCulture),
        s.MarginV.ToString(CultureInfo.InvariantCulture), s.Encoding.ToString(CultureInfo.InvariantCulture));

    private static Cue? ParseDialogue(string value, string[] format, int id)
    {
        // Text is the last field and may itself contain commas.
        var parts = value.Split(',', format.Length);
        if (parts.Length < format.Length)
        {
            return null;
        }

        var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < format.Length; i++)
        {
            f[format[i]] = i == format.Length - 1 ? parts[i] : parts[i].Trim();
        }

        if (!SubtitleTime.TryParseAss(Str(f, "Start", string.Empty), out var start) ||
            !SubtitleTime.TryParseAss(Str(f, "End", string.Empty), out var end))
        {
            return null;
        }

        var text = Str(f, "Text", string.Empty)
            .Replace("\\N", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal);
        var speaker = Str(f, "Name", string.Empty);
        var style = Str(f, "Style", "Default");
        var effect = Str(f, "Effect", string.Empty);

        return new Cue(id, start, end, SubtitleText.NormalizeLines(text))
        {
            Speaker = speaker.Length > 0 ? speaker : null,
            Format = new CueFormatData
            {
                Style = style.TrimStart('*'),
                Layer = TryInt(f, "Layer"),
                MarginL = TryInt(f, "MarginL"),
                MarginR = TryInt(f, "MarginR"),
                MarginV = TryInt(f, "MarginV"),
                Effect = effect.Length > 0 ? effect : null,
            },
        };
    }

    private static string[] SplitFormat(string value)
        => [.. value.Split(',').Select(p => p.Trim())];

    // Commas would shift fields; line breaks would end the line.
    private static string Sanitize(string? value)
        => (value ?? string.Empty).Replace(',', ';').Replace('\n', ' ').Trim();

    private static string Str(Dictionary<string, string> f, string key, string fallback)
        => f.TryGetValue(key, out var v) ? v : fallback;

    private static int Int(Dictionary<string, string> f, string key, int fallback)
        => f.TryGetValue(key, out var v) ? (int)double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture) : fallback;

    private static int TryInt(Dictionary<string, string> f, string key)
        => f.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static double Dbl(Dictionary<string, string> f, string key, double fallback)
        => f.TryGetValue(key, out var v) ? double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture) : fallback;

    private static bool Bool(Dictionary<string, string> f, string key)
        => f.TryGetValue(key, out var v) && v != "0";

    private static AssColor Color(Dictionary<string, string> f, string key, AssColor fallback)
        => f.TryGetValue(key, out var v) && AssColor.TryParseAss(v, out var c) ? c : fallback;

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
