using System.Buffers.Binary;
using System.Text;
using LingoCue.Core.Fonts;
using LingoCue.Core.Platform;
using Shouldly;

namespace LingoCue.Core.Tests.Fonts;

/// <summary>Builds tiny but valid sfnt files (name + cmap format 4 tables) so no font binaries live in the repo.</summary>
internal static class TestFont
{
    public static byte[] Ttf(string family, string? typographic = null, string characters = "abcçÇğĞıİöÖşŞüÜ")
        => Face(0, family, typographic, characters);

    public static byte[] Ttc(params (string Family, string Characters)[] faces)
    {
        var header = 12 + (4 * faces.Length);
        var parts = new List<byte[]>();
        var offsets = new List<int>();
        var position = header;
        foreach (var (family, characters) in faces)
        {
            offsets.Add(position);
            var face = Face(position, family, null, characters);
            parts.Add(face);
            position += face.Length;
        }

        var bytes = new byte[position];
        Encoding.ASCII.GetBytes("ttcf").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)faces.Length);
        for (var i = 0; i < faces.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12 + (4 * i)), (uint)offsets[i]);
            parts[i].CopyTo(bytes, offsets[i]);
        }

        return bytes;
    }

    /// <param name="baseOffset">Where this face starts in the file (table offsets are absolute).</param>
    private static byte[] Face(int baseOffset, string family, string? typographic, string characters)
    {
        var cmap = Cmap(characters);
        var name = Name(family, "Regular", typographic);
        const int directory = 12 + (2 * 16);
        var cmapAt = directory;
        var nameAt = cmapAt + cmap.Length;
        var bytes = new byte[nameAt + name.Length];
        var s = bytes.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(s, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(s[4..], 2);
        WriteRecord(s[12..], "cmap", baseOffset + cmapAt, cmap.Length);
        WriteRecord(s[28..], "name", baseOffset + nameAt, name.Length);
        cmap.CopyTo(bytes, cmapAt);
        name.CopyTo(bytes, nameAt);
        return bytes;
    }

    private static void WriteRecord(Span<byte> at, string tag, int offset, int length)
    {
        Encoding.ASCII.GetBytes(tag).CopyTo(at);
        BinaryPrimitives.WriteUInt32BigEndian(at[8..], (uint)offset);
        BinaryPrimitives.WriteUInt32BigEndian(at[12..], (uint)length);
    }

    private static byte[] Name(string family, string subfamily, string? typographic)
    {
        var entries = new List<(int Id, byte[] Text)> { (1, Utf16(family)), (2, Utf16(subfamily)) };
        if (typographic is not null)
        {
            entries.Add((16, Utf16(typographic)));
        }

        var storage = 6 + (12 * entries.Count);
        var bytes = new byte[storage + entries.Sum(e => e.Text.Length)];
        var s = bytes.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(s[2..], (ushort)entries.Count);
        BinaryPrimitives.WriteUInt16BigEndian(s[4..], (ushort)storage);
        var offset = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var r = s[(6 + (12 * i))..];
            BinaryPrimitives.WriteUInt16BigEndian(r, 3);          // Windows
            BinaryPrimitives.WriteUInt16BigEndian(r[2..], 1);     // Unicode BMP
            BinaryPrimitives.WriteUInt16BigEndian(r[4..], 0x409); // English (US)
            BinaryPrimitives.WriteUInt16BigEndian(r[6..], (ushort)entries[i].Id);
            BinaryPrimitives.WriteUInt16BigEndian(r[8..], (ushort)entries[i].Text.Length);
            BinaryPrimitives.WriteUInt16BigEndian(r[10..], (ushort)offset);
            entries[i].Text.CopyTo(bytes, storage + offset);
            offset += entries[i].Text.Length;
        }

        return bytes;
    }

    private static byte[] Cmap(string characters)
    {
        var codes = characters.Select(c => (int)c).Distinct().Order().ToList();
        var segments = codes.Count + 1; // + the final 0xFFFF segment
        var sub = 16 + (8 * segments);
        var bytes = new byte[12 + sub];
        var s = bytes.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(s[2..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(s[4..], 3);
        BinaryPrimitives.WriteUInt16BigEndian(s[6..], 1);
        BinaryPrimitives.WriteUInt32BigEndian(s[8..], 12);
        var t = s[12..];
        BinaryPrimitives.WriteUInt16BigEndian(t, 4);
        BinaryPrimitives.WriteUInt16BigEndian(t[2..], (ushort)sub);
        BinaryPrimitives.WriteUInt16BigEndian(t[6..], (ushort)(2 * segments));
        var ends = 14;
        var starts = ends + (2 * segments) + 2;
        var deltas = starts + (2 * segments);
        for (var i = 0; i < segments; i++)
        {
            var code = i < codes.Count ? codes[i] : 0xFFFF;
            var glyph = i < codes.Count ? i + 1 : 0;
            BinaryPrimitives.WriteUInt16BigEndian(t[(ends + (2 * i))..], (ushort)code);
            BinaryPrimitives.WriteUInt16BigEndian(t[(starts + (2 * i))..], (ushort)code);
            BinaryPrimitives.WriteUInt16BigEndian(t[(deltas + (2 * i))..], (ushort)((glyph - code) & 0xFFFF));
        }

        return bytes;
    }

    private static byte[] Utf16(string text) => Encoding.BigEndianUnicode.GetBytes(text);
}

public sealed class FontFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-fonts-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Reads_legacy_and_typographic_family_and_turkish_coverage()
    {
        var path = Write("black.ttf", TestFont.Ttf("Deneme Black", "Deneme"));

        var face = FontFileReader.ReadFaces(path).ShouldHaveSingleItem();

        face.Family.ShouldBe("Deneme Black");
        face.TypographicFamily.ShouldBe("Deneme");
        face.Subfamily.ShouldBe("Regular");
        face.HasTurkishLetters.ShouldBeTrue();
    }

    [Fact]
    public void Detects_missing_turkish_letters()
    {
        var path = Write("ascii.ttf", TestFont.Ttf("Ascii Only", characters: "abcdefghijklmnopqrstuvwxyz"));

        FontFileReader.ReadFaces(path).ShouldHaveSingleItem().HasTurkishLetters.ShouldBeFalse();
    }

    [Fact]
    public void Detects_korean_letters()
    {
        var korean = Write("kr.ttf", TestFont.Ttf("Kore Deneme", characters: "abc" + FontFileReader.HangulSample));
        var latin = Write("latin.ttf", TestFont.Ttf("Latin Deneme"));

        FontFileReader.ReadFaces(korean).ShouldHaveSingleItem().HasHangul.ShouldBeTrue();
        FontFileReader.ReadFaces(latin).ShouldHaveSingleItem().HasHangul.ShouldBeFalse();
    }

    [Fact]
    public void Suggests_a_preferred_korean_font_then_any_with_hangul()
    {
        FontCatalog.PickHangulFamily([("Arial", false), ("Zeta Hangul", true), ("Malgun Gothic", true)]).ShouldBe("Malgun Gothic");
        FontCatalog.PickHangulFamily([("Arial", false), ("Zeta Hangul", true)]).ShouldBe("Zeta Hangul");
        FontCatalog.PickHangulFamily([("Arial", false)]).ShouldBeNull();
    }

    [Fact]
    public void Reads_every_face_of_a_collection()
    {
        var path = Write("pair.ttc", TestFont.Ttc(("Bir", "ğışİçöüÇĞŞÖÜ"), ("İki", "abc")));

        var faces = FontFileReader.ReadFaces(path);

        faces.Select(f => f.Family).ShouldBe(["Bir", "İki"]);
        faces.Select(f => f.HasTurkishLetters).ShouldBe([true, false]);
    }

    [Fact]
    public void Non_fonts_are_ignored()
    {
        FontFileReader.ReadFaces(Write("bad.ttf", Encoding.UTF8.GetBytes("not a font"))).ShouldBeEmpty();
        FontFileReader.ReadFaces(Path.Combine(_dir, "missing.ttf")).ShouldBeEmpty();
        FontFileReader.IsFontFile("x.OTF").ShouldBeTrue();
        FontFileReader.IsFontFile("x.woff").ShouldBeFalse();
    }

    [Fact]
    public void Catalog_lists_system_and_project_fonts_and_prefers_the_project_copy()
    {
        Write(Path.Combine("system", "a.ttf"), TestFont.Ttf("Sistem Fontu"));
        Write(Path.Combine("system", "sub", "b.ttf"), TestFont.Ttf("Ortak Font", characters: "abc"));
        var project = Path.Combine(_dir, "proje");
        Write(Path.Combine("proje", "fonts", "c.ttf"), TestFont.Ttf("Ortak Font"));
        var catalog = new FontCatalog(new FakePlatform([Path.Combine(_dir, "system"), Path.Combine(_dir, "nope")]));
        var fonts = FontCatalog.ProjectFontDirectory(Path.Combine(project, "film.lingocue"));

        fonts.ShouldBe(Path.Combine(project, "fonts"));
        catalog.Families(fonts).ShouldBe(["Ortak Font", "Sistem Fontu"]);
        catalog.Check("ortak font", fonts).ShouldBe(new FontStatus(FontSource.Project, true));
        catalog.Check("Ortak Font", null).ShouldBe(new FontStatus(FontSource.System, false));
        catalog.Check("Yok", fonts).Source.ShouldBe(FontSource.Missing);
        catalog.FamiliesIn(Path.Combine(project, "fonts", "c.ttf")).ShouldBe(["Ortak Font"]);
    }

    [Fact]
    public void Catalog_sees_fonts_added_to_the_project_later()
    {
        var catalog = new FontCatalog(new FakePlatform([]));
        var fonts = Path.Combine(_dir, "fonts");
        Directory.CreateDirectory(fonts);
        catalog.Families(fonts).ShouldBeEmpty();

        Write(Path.Combine("fonts", "new.ttf"), TestFont.Ttf("Yeni"));
        Directory.SetLastWriteTimeUtc(fonts, DateTime.UtcNow.AddSeconds(5)); // coarse file-system clocks

        catalog.Families(fonts).ShouldBe(["Yeni"]);
    }

    private sealed class FakePlatform(string[] fontDirectories) : IPlatformInfo
    {
        public OsPlatform Platform => OsPlatform.Windows;

        public string RuntimeIdentifier => "test";

        public string AppDataDirectory => Path.GetTempPath();

        public string LogDirectory => Path.GetTempPath();

        public IReadOnlyList<string> ToolSearchDirectories => [];

        public string NativeLibraryDirectory => AppContext.BaseDirectory;

        public IReadOnlyList<string> FontDirectories => fontDirectories;

        public string GetExecutableFileName(string name) => name;
    }
}
