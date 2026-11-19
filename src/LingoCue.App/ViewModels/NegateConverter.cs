using System.Globalization;
using Avalonia.Data.Converters;

namespace LingoCue.App.ViewModels;

/// <summary>Flips a number's sign (a slider whose "better" end is on the right while the value goes down).</summary>
public sealed class NegateConverter : IValueConverter
{
    public static NegateConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? -d : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? -d : value;
}
