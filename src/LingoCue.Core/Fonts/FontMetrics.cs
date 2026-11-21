using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;

namespace LingoCue.Core.Fonts;

/// <summary>Measures the width of a subtitle line the way libass lays it out (in script pixels).</summary>
public interface ITextMeasurer
{
    /// <returns>Width of the longest line of <paramref name="text"/>, or null when the font cannot be measured.</returns>
    double? LongestLineWidth(string text, SubtitleStyle style);
}

/// <summary>
/// Advance widths from a font file (<c>head</c>, <c>hhea</c>, <c>OS/2</c>, <c>hmtx</c>, <c>cmap</c>). Like
/// libass (VSFilter compatible), one font unit is <c>size / (usWinAscent + usWinDescent)</c> pixels.
/// Kerning is not applied, so widths can be off by a few pixels; callers pad for that.
/// </summary>
public sealed class FontMetrics
{
    private static readonly ConcurrentDictionary<(string Path, int Index), FontMetrics?> s_cache = new();
    private readonly ushort[] _advances;
    private readonly Dictionary<int, int> _glyphs;

    private FontMetrics(int winHeight, ushort[] advances, Dictionary<int, int> glyphs)
    {
        WinHeight = winHeight;
        _advances = advances;
        _glyphs = glyphs;
    }

    /// <summary><c>usWinAscent + usWinDescent</c> (or hhea ascender − descender without an OS/2 table).</summary>
    public int WinHeight { get; }

    public static FontMetrics? Load(string path, int index = 0)
        => s_cache.GetOrAdd((path, index), key => Read(key.Path, key.Index));

    /// <summary>Width in font units; characters missing from the font count as a space-sized gap.</summary>
    public double Width(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        double units = 0;
        var space = Advance(' ') ?? WinHeight / 4;
        for (var i = 0; i < line.Length; i++)
        {
            var c = char.IsSurrogatePair(line, i) ? char.ConvertToUtf32(line, i++) : line[i];
            units += Advance(c) ?? space;
        }

        return units;
    }

    private int? Advance(int c)
    {
        if (!_glyphs.TryGetValue(c, out var glyph) || glyph == 0)
        {
            return null;
        }

        return _advances.Length == 0 ? null : _advances[Math.Min(glyph, _advances.Length - 1)];
    }

    private static FontMetrics? Read(string path, int index)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var s = bytes.AsSpan();
            var offset = 0;
            if (s.Length >= 16 && s[..4].SequenceEqual("ttcf"u8))
            {
                if (index >= BinaryPrimitives.ReadUInt32BigEndian(s[8..]))
                {
                    return null;
                }

                offset = (int)BinaryPrimitives.ReadUInt32BigEndian(s[(12 + (4 * index))..]);
            }

            var tables = new Dictionary<string, int>(StringComparer.Ordinal);
            var count = BinaryPrimitives.ReadUInt16BigEndian(s[(offset + 4)..]);
            for (var i = 0; i < count; i++)
            {
                var record = offset + 12 + (16 * i);
                tables[System.Text.Encoding.ASCII.GetString(bytes, record, 4)] = (int)BinaryPrimitives.ReadUInt32BigEndian(s[(record + 8)..]);
            }

            if (!tables.TryGetValue("hhea", out var hhea) || !tables.TryGetValue("hmtx", out var hmtx) || !tables.TryGetValue("cmap", out var cmap))
            {
                return null;
            }

            var winHeight = tables.TryGetValue("OS/2", out var os2)
                ? BinaryPrimitives.ReadUInt16BigEndian(s[(os2 + 74)..]) + BinaryPrimitives.ReadUInt16BigEndian(s[(os2 + 76)..])
                : BinaryPrimitives.ReadInt16BigEndian(s[(hhea + 4)..]) - BinaryPrimitives.ReadInt16BigEndian(s[(hhea + 6)..]);
            var metrics = BinaryPrimitives.ReadUInt16BigEndian(s[(hhea + 34)..]);
            var advances = new ushort[metrics];
            for (var i = 0; i < metrics; i++)
            {
                advances[i] = BinaryPrimitives.ReadUInt16BigEndian(s[(hmtx + (4 * i))..]);
            }

            var glyphs = ReadCmap(s, cmap);
            return winHeight > 0 && glyphs.Count > 0 ? new FontMetrics(winHeight, advances, glyphs) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Unicode → glyph id from a format 12 (full Unicode) or format 4 (BMP) subtable.</summary>
    private static Dictionary<int, int> ReadCmap(ReadOnlySpan<byte> s, int cmap)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(s[(cmap + 2)..]);
        int format4 = -1, format12 = -1;
        for (var i = 0; i < count; i++)
        {
            var rec = cmap + 4 + (8 * i);
            int platform = BinaryPrimitives.ReadUInt16BigEndian(s[rec..]), encoding = BinaryPrimitives.ReadUInt16BigEndian(s[(rec + 2)..]);
            var sub = cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(s[(rec + 4)..]);
            var format = BinaryPrimitives.ReadUInt16BigEndian(s[sub..]);
            if (format == 12 && (platform == 0 || (platform == 3 && encoding == 10)))
            {
                format12 = sub;
            }
            else if (format == 4 && (platform == 0 || (platform == 3 && encoding is 1 or 0)))
            {
                format4 = sub;
            }
        }

        var map = new Dictionary<int, int>();
        if (format12 >= 0)
        {
            var groups = BinaryPrimitives.ReadUInt32BigEndian(s[(format12 + 12)..]);
            for (var g = 0; g < groups; g++)
            {
                var at = format12 + 16 + (12 * g);
                uint start = BinaryPrimitives.ReadUInt32BigEndian(s[at..]), end = BinaryPrimitives.ReadUInt32BigEndian(s[(at + 4)..]);
                var glyph = BinaryPrimitives.ReadUInt32BigEndian(s[(at + 8)..]);
                for (var c = start; c <= end && c - start < 0x10000; c++)
                {
                    map[(int)c] = (int)(glyph + (c - start));
                }
            }

            return map;
        }

        if (format4 < 0)
        {
            return map;
        }

        var segments = BinaryPrimitives.ReadUInt16BigEndian(s[(format4 + 6)..]) / 2;
        var ends = format4 + 14;
        var starts = ends + (2 * segments) + 2;
        var deltas = starts + (2 * segments);
        var rangeOffsets = deltas + (2 * segments);
        for (var i = 0; i < segments; i++)
        {
            int end = BinaryPrimitives.ReadUInt16BigEndian(s[(ends + (2 * i))..]);
            int start = BinaryPrimitives.ReadUInt16BigEndian(s[(starts + (2 * i))..]);
            int delta = BinaryPrimitives.ReadUInt16BigEndian(s[(deltas + (2 * i))..]);
            int rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(s[(rangeOffsets + (2 * i))..]);
            for (var c = start; c <= end && c != 0xFFFF; c++)
            {
                var glyph = rangeOffset == 0
                    ? (c + delta) & 0xFFFF
                    : BinaryPrimitives.ReadUInt16BigEndian(s[(rangeOffsets + (2 * i) + rangeOffset + (2 * (c - start)))..]);
                if (glyph != 0)
                {
                    map[c] = glyph;
                }
            }
        }

        return map;
    }
}

/// <summary>Measures with the installed or project font that libass would pick for the style.</summary>
public sealed class FontTextMeasurer(IFontCatalog catalog, string? projectFontDirectory) : ITextMeasurer
{
    public double? LongestLineWidth(string text, SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        if (catalog.FindFace(style.FontName, style.Bold, style.Italic, projectFontDirectory) is not { } face ||
            FontMetrics.Load(face.Path, face.Index) is not { } metrics)
        {
            return null;
        }

        var scale = style.FontSize / metrics.WinHeight * style.ScaleX / 100;
        // No bold face: libass makes the regular one bolder, which widens each glyph a little.
        var synthBold = style.Bold && !face.Subfamily.Contains("Bold", StringComparison.OrdinalIgnoreCase) ? style.FontSize * 0.04 : 0;
        double widest = 0;
        foreach (var line in SubtitleText.StripTags(text).Split('\n'))
        {
            var glyphs = new StringInfo(line).LengthInTextElements;
            var width = (metrics.Width(line) * scale) + (glyphs * (style.Spacing + synthBold));
            widest = Math.Max(widest, width);
        }

        return widest;
    }
}
