using System.Text;
using LingoCue.Core.Formats;

namespace LingoCue.Core.Tests.Formats;

public sealed class SubtitleFileTests : IDisposable
{
    private const string TurkishSrt = "1\n00:00:01,000 --> 00:00:02,000\nĞüşıöç İĞÜŞÖÇ\n";
    private readonly string _dir = Directory.CreateTempSubdirectory("lingocue-subfile-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Decodes_utf8_without_bom()
    {
        var (text, encoding) = SubtitleFile.Decode(Encoding.UTF8.GetBytes(TurkishSrt));

        text.ShouldBe(TurkishSrt);
        encoding.WebName.ShouldBe("utf-8");
    }

    [Fact]
    public void Decodes_utf8_and_utf16_boms()
    {
        SubtitleFile.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(TurkishSrt)]).Text.ShouldBe(TurkishSrt);
        SubtitleFile.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes(TurkishSrt)]).Text.ShouldBe(TurkishSrt);
        SubtitleFile.Decode([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(TurkishSrt)]).Text.ShouldBe(TurkishSrt);
    }

    [Fact]
    public void Falls_back_to_windows_1254_for_legacy_turkish_files()
    {
        var legacy = SubtitleFile.TurkishAnsi.GetBytes(TurkishSrt);

        var (text, encoding) = SubtitleFile.Decode(legacy);

        text.ShouldBe(TurkishSrt);
        encoding.CodePage.ShouldBe(1254);
    }

    [Theory]
    [InlineData(false, LineEnding.CrLf)]
    [InlineData(true, LineEnding.Lf)]
    public void Encodes_with_requested_bom_and_line_endings(bool bom, LineEnding ending)
    {
        var bytes = SubtitleFile.Encode("a\nb\n", new SubtitleWriteOptions { Utf8Bom = bom, LineEnding = ending });

        bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]).ShouldBe(bom);
        var text = Encoding.UTF8.GetString(bytes.AsSpan(bom ? 3 : 0));
        text.ShouldBe(ending == LineEnding.CrLf ? "a\r\nb\r\n" : "a\nb\n");
    }

    [Fact]
    public async Task Writes_and_reads_back_every_format()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = SubtitleFormats.Srt.Parse(TurkishSrt);

        foreach (var ext in new[] { ".srt", ".vtt", ".ass" })
        {
            var path = Path.Combine(_dir, "out" + ext);
            await SubtitleFile.WriteAsync(path, doc, cancellationToken: ct);

            var read = await SubtitleFile.ReadAsync(path, ct);

            read.Format.Extensions.ShouldContain(ext);
            read.Document.Cues.ShouldHaveSingleItem().Text.ShouldBe("Ğüşıöç İĞÜŞÖÇ");
        }

        Directory.GetFiles(_dir, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task Detects_format_by_content_when_extension_lies()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_dir, "actually-vtt.srt");
        await File.WriteAllTextAsync(path, "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nx\n", ct);

        (await SubtitleFile.ReadAsync(path, ct)).Format.ShouldBeOfType<VttFormat>();
    }

    [Fact]
    public async Task Unknown_content_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_dir, "notes.txt");
        await File.WriteAllTextAsync(path, "just some notes", ct);

        await Should.ThrowAsync<SubtitleFormatException>(() => SubtitleFile.ReadAsync(path, ct));
    }
}
