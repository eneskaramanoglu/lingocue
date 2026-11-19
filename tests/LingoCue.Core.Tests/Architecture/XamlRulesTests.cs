using System.Text.RegularExpressions;

namespace LingoCue.Core.Tests.Architecture;

/// <summary>Rules for the app's XAML that the compiler does not catch.</summary>
public partial class XamlRulesTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LingoCue.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (LingoCue.slnx) not found.");
    }

    /// <summary>
    /// <c>Run.Text</c> binds two-way by default in Avalonia. A display-only Run would write its text back
    /// to the model; with a <c>StringFormat</c> the formatted text grew on every round trip until the
    /// stack overflowed (and the track language was overwritten). Every Run binding must be one-way.
    /// </summary>
    [Fact]
    public void Run_bindings_are_one_way()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .SelectMany(file => RunBinding().Matches(File.ReadAllText(file))
                .Where(m => !m.Value.Contains("Mode=OneWay", StringComparison.Ordinal))
                .Select(m => $"{Path.GetFileName(file)}: {m.Value}"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [GeneratedRegex("""<Run\b[^>]*Text="\{Binding[^"]*"[^>]*>""")]
    private static partial Regex RunBinding();
}
