using System.Buffers.Binary;
using System.Text;

namespace LingoCue.Core.Fonts;

/// <summary>One face inside a font file.</summary>
/// <param name="Family">Legacy (GDI) family name, name ID 1 — the name libass and ASS styles use, e.g. "Arial Black".</param>
/// <param name="TypographicFamily">Name ID 16 when present (e.g. "Arial"), otherwise the family.</param>
/// <param name="Subfamily">Style name, name ID 2 (e.g. "Bold Italic").</param>
public sealed record FontFace(string Path, int Index, string Family, string TypographicFamily, string Subfamily, bool HasTurkishLetters);

/// <summary>
/// Reads family names and character coverage straight from TrueType/OpenType files (<c>.ttf</c>,
/// <c>.otf</c>, <c>.ttc</c>): only the table directory, the <c>name</c> table and the Unicode
/// <c>cmap</c> subtable are read, so scanning a system font folder stays fast.
/// </summary>
public static class FontFileReader
{
    /// <summary>Turkish letters that some fonts lack (the ASCII ones are everywhere).</summary>
    public const string TurkishLetters = "çÇğĞıİöÖşŞüÜ";

    public static bool IsFontFile(string path)
        => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc" or ".otc";

    /// <summary>Faces in the file; empty when the file is not a readable font.</summary>
    public static IReadOnlyList<FontFace> ReadFaces(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            var reader = new Reader(stream);
            var tag = reader.Tag(0);
            if (tag == "ttcf")
            {
                var count = (int)Math.Min(reader.U32(8), 256);
                var faces = new List<FontFace>(count);
                for (var i = 0; i < count; i++)
                {
                    if (ReadFace(reader, path, i, reader.U32(12 + (4 * i))) is { } face)
                    {
                        faces.Add(face);
                    }
                }

                return faces;
            }

            return ReadFace(reader, path, 0, 0) is { } single ? [single] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentOutOfRangeException or OverflowException)
        {
            return [];
        }
    }

    private static FontFace? ReadFace(Reader r, string path, int index, long offset)
    {
        var numTables = r.U16(offset + 4);
        long nameTable = -1, cmapTable = -1;
        for (var i = 0; i < numTables; i++)
        {
            var record = offset + 12 + (16 * i);
            switch (r.Tag(record))
            {
                case "name":
                    nameTable = r.U32(record + 8);
                    break;
                case "cmap":
                    cmapTable = r.U32(record + 8);
                    break;
            }
        }

        if (nameTable < 0)
        {
            return null;
        }

        var names = ReadNames(r, nameTable);
        if (!names.TryGetValue(1, out var family) || string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        var typographic = names.TryGetValue(16, out var t) && !string.IsNullOrWhiteSpace(t) ? t : family;
        var subfamily = names.TryGetValue(2, out var s) ? s : "Regular";
        var turkish = cmapTable >= 0 && HasAll(r, cmapTable, TurkishLetters);
        return new FontFace(path, index, family.Trim(), typographic.Trim(), subfamily.Trim(), turkish);
    }

    /// <summary>Name IDs 1, 2 and 16, preferring Windows/English, then any Windows, then Mac Roman.</summary>
    private static Dictionary<int, string> ReadNames(Reader r, long table)
    {
        var count = r.U16(table + 2);
        var storage = table + r.U16(table + 4);
        var best = new Dictionary<int, (int Rank, string Value)>();
        for (var i = 0; i < count; i++)
        {
            var rec = table + 6 + (12 * i);
            int platform = r.U16(rec), encoding = r.U16(rec + 2), language = r.U16(rec + 4), nameId = r.U16(rec + 6);
            if (nameId is not (1 or 2 or 16))
            {
                continue;
            }

            int length = r.U16(rec + 8), start = r.U16(rec + 10);
            var rank = platform switch
            {
                3 when encoding is 1 or 10 => language == 0x409 ? 0 : 1,
                0 => 2,
                1 when encoding == 0 => 3,
                _ => -1,
            };
            if (rank < 0 || (best.TryGetValue(nameId, out var current) && current.Rank <= rank))
            {
                continue;
            }

            var bytes = r.Bytes(storage + start, length);
            var value = rank <= 2 ? Encoding.BigEndianUnicode.GetString(bytes) : Encoding.Latin1.GetString(bytes);
            best[nameId] = (rank, value);
        }

        return best.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
    }

    private static bool HasAll(Reader r, long cmap, string characters)
    {
        var count = r.U16(cmap + 2);
        long format4 = -1, format12 = -1;
        for (var i = 0; i < count; i++)
        {
            var rec = cmap + 4 + (8 * i);
            int platform = r.U16(rec), encoding = r.U16(rec + 2);
            var sub = cmap + r.U32(rec + 4);
            var format = r.U16(sub);
            if (format == 12 && (platform == 0 || (platform == 3 && encoding == 10)))
            {
                format12 = sub;
            }
            else if (format == 4 && (platform == 0 || (platform == 3 && encoding is 1 or 0)))
            {
                format4 = sub;
            }
        }

        if (format12 >= 0)
        {
            return characters.All(c => HasGlyph12(r, format12, c));
        }

        return format4 >= 0 && characters.All(c => HasGlyph4(r, format4, c));
    }

    private static bool HasGlyph12(Reader r, long table, int c)
    {
        var groups = r.U32(table + 12);
        for (long g = 0; g < groups; g++)
        {
            var at = table + 16 + (12 * g);
            uint start = r.U32(at), end = r.U32(at + 4);
            if (c >= start && c <= end)
            {
                return r.U32(at + 8) + (c - start) != 0;
            }
        }

        return false;
    }

    private static bool HasGlyph4(Reader r, long table, int c)
    {
        var segments = r.U16(table + 6) / 2;
        var ends = table + 14;
        var starts = ends + (2 * segments) + 2;
        var deltas = starts + (2 * segments);
        var rangeOffsets = deltas + (2 * segments);
        for (var i = 0; i < segments; i++)
        {
            if (r.U16(ends + (2 * i)) < c)
            {
                continue;
            }

            var start = r.U16(starts + (2 * i));
            if (start > c)
            {
                return false;
            }

            var delta = r.U16(deltas + (2 * i));
            var rangeOffset = r.U16(rangeOffsets + (2 * i));
            if (rangeOffset == 0)
            {
                return ((c + delta) & 0xFFFF) != 0;
            }

            var glyph = r.U16(rangeOffsets + (2 * i) + rangeOffset + (2 * (c - start)));
            return glyph != 0;
        }

        return false;
    }

    /// <summary>Big-endian reads at absolute offsets.</summary>
    private sealed class Reader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[4];

        public string Tag(long at) => Encoding.ASCII.GetString(Bytes(at, 4));

        public ushort U16(long at)
        {
            Read(at, 2);
            return BinaryPrimitives.ReadUInt16BigEndian(_buffer);
        }

        public uint U32(long at)
        {
            Read(at, 4);
            return BinaryPrimitives.ReadUInt32BigEndian(_buffer);
        }

        public byte[] Bytes(long at, int length)
        {
            var bytes = new byte[length];
            stream.Position = at;
            stream.ReadExactly(bytes);
            return bytes;
        }

        private void Read(long at, int length)
        {
            stream.Position = at;
            stream.ReadExactly(_buffer, 0, length);
        }
    }
}
