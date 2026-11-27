using Avalonia.Data.Converters;
using LingoCue.Core.Text;

namespace LingoCue.App.Views;

/// <summary>Small one-way converters used from XAML.</summary>
public static class Converters
{
    /// <summary>Language code → badge text (<c>en-US</c> → <c>EN</c>).</summary>
    public static FuncValueConverter<string?, string> LanguageBadge { get; } = new(LanguageCodes.Badge);
}
