namespace LingoCue.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void Project_file_extension_is_lowercase_with_leading_dot()
    {
        AppInfo.ProjectFileExtension.ShouldStartWith(".");
        AppInfo.ProjectFileExtension.ShouldBe(AppInfo.ProjectFileExtension.ToLowerInvariant());
    }

    [Fact]
    public void Version_is_semantic_three_part()
        => AppInfo.Version.Split('.').Length.ShouldBe(3);
}
