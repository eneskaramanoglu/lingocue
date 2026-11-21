using LingoCue.Core.Diagnostics;

namespace LingoCue.Core.Tests.Diagnostics;

public class DiagnosticsReportTests
{
    [Theory]
    [InlineData(@"C:\Users\ayse\AppData\Roaming\LingoCue\logs", @"~\AppData\Roaming\LingoCue\logs")]
    [InlineData("opened C:/Users/ayse/Videos/tatil.mp4", "opened ~/Videos/tatil.mp4")]
    [InlineData(@"{""path"":""C:\\Users\\ayse\\film.lingocue""}", @"{""path"":""~\\film.lingocue""}")] // JSON-escaped
    [InlineData(@"c:\users\AYSE\x", @"~\x")]                                                             // any case
    [InlineData("nothing personal", "nothing personal")]
    public void Masks_the_home_folder(string text, string expected)
        => DiagnosticsReport.Redact(text, @"C:\Users\ayse").ShouldBe(expected);

    [Fact]
    public void Masks_a_macos_home_folder()
        => DiagnosticsReport.Redact("/Users/ayse/Movies/a.mov", "/Users/ayse").ShouldBe("~/Movies/a.mov");

    [Fact]
    public void Settings_leave_out_recent_files()
    {
        const string json = """{"Theme":"Dark","RecentFiles":["C:\\Users\\ayse\\gizli.mp4","b.lingocue"],"Llm":{"Provider":"gemini"}}""";

        var sanitized = DiagnosticsReport.SanitizeSettings(json);

        sanitized.ShouldNotContain("gizli");
        sanitized.ShouldContain("(2 files left out)");
        sanitized.ShouldContain("\"Provider\": \"gemini\"");
        DiagnosticsReport.SanitizeSettings("{ broken").ShouldContain("could not be read");
    }

    [Fact]
    public void Summary_lists_one_fact_per_line()
    {
        var summary = DiagnosticsReport.Summary([new("LingoCue", "1.0.0"), new("FFmpeg", null)]);

        summary.ShouldContain("LingoCue: 1.0.0");
        summary.ShouldContain("FFmpeg: —");
    }
}
