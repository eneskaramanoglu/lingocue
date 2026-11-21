using LingoCue.Core.Settings;
using LingoCue.Core.Updates;

namespace LingoCue.Core.Tests.Updates;

public class ReleaseVersionsTests
{
    [Theory]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.12.0", "1.0.0", false)]
    [InlineData("v1.10.0", "1.9.3", true)]   // numbers, not text
    [InlineData("v1.0.0", "1.0.0-beta.2", true)]
    [InlineData("v1.0.0-beta.3", "1.0.0-beta.2", true)]
    [InlineData("v1.0.0-beta.1", "1.0.0", false)]
    [InlineData("v1.0.0+build.5", "1.0.0", false)]
    [InlineData("nightly", "1.0.0", false)]
    public void Compares_versions(string candidate, string current, bool newer)
        => ReleaseVersions.IsNewer(candidate, current).ShouldBe(newer);

    [Fact]
    public void Reads_githubs_latest_release()
    {
        const string json = """
            {"tag_name":"v1.2.0","html_url":"https://github.com/eneskaramanoglu/lingocue/releases/tag/v1.2.0",
             "draft":false,"prerelease":false,"body":"### Added\n- Something"}
            """;

        var update = ReleaseVersions.ParseLatestRelease(json).ShouldNotBeNull();

        update.Version.ShouldBe("1.2.0");
        update.ReleasePage.ShouldBe(new Uri("https://github.com/eneskaramanoglu/lingocue/releases/tag/v1.2.0"));
        update.Notes!.ShouldContain("Something");
        update.CanInstallInPlace.ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"tag_name":"v2.0.0-rc.1","prerelease":true}""")]
    [InlineData("""{"tag_name":"v2.0.0","draft":true}""")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Ignores_drafts_prereleases_and_errors(string json)
        => ReleaseVersions.ParseLatestRelease(json).ShouldBeNull();

    [Fact]
    public void Only_github_pages_are_opened()
    {
        var update = ReleaseVersions.ParseLatestRelease("""{"tag_name":"v1.2.0","html_url":"https://evil.example/x"}""").ShouldNotBeNull();

        update.ReleasePage.ShouldBe(ReleaseVersions.ReleasePage("1.2.0"));
    }

    [Fact]
    public void Checks_at_most_about_once_a_day()
    {
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

        new UpdateSettings().IsCheckDue(now).ShouldBeTrue();
        new UpdateSettings { LastCheckUtc = now.AddHours(-2) }.IsCheckDue(now).ShouldBeFalse();
        new UpdateSettings { LastCheckUtc = now.AddDays(-1) }.IsCheckDue(now).ShouldBeTrue();
        new UpdateSettings { AutoCheck = false }.IsCheckDue(now).ShouldBeFalse();
    }
}
