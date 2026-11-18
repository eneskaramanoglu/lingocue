using System.Reflection;

namespace LingoCue.Core.Tests.Architecture;

/// <summary>Core libraries must never depend on the UI framework.</summary>
public class LayeringTests
{
    public static TheoryData<string> CoreLibraries() => new()
    {
        "LingoCue.Core",
        "LingoCue.Media",
        "LingoCue.Transcription",
        "LingoCue.Llm",
        "LingoCue.Infrastructure",
    };

    [Theory]
    [MemberData(nameof(CoreLibraries))]
    public void Library_does_not_reference_avalonia(string assemblyName)
    {
        var references = Assembly.Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        references.ShouldNotContain(r => r.StartsWith("Avalonia", StringComparison.Ordinal));
        references.ShouldNotContain("LingoCue");
    }

    [Fact]
    public void Core_has_no_project_dependencies()
        => Assembly.Load("LingoCue.Core").GetReferencedAssemblies()
            .Select(a => a.Name)
            .ShouldNotContain(n => n != null && n.StartsWith("LingoCue.", StringComparison.Ordinal));
}
