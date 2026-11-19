using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LingoCue.App.Views;

/// <summary>
/// Binds a group of radio buttons to one number: checked when the value equals the parameter;
/// checking a button writes its parameter back (unchecking writes nothing).
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public static EqualsConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null &&
           string.Equals(System.Convert.ToString(value, CultureInfo.InvariantCulture), System.Convert.ToString(parameter, CultureInfo.InvariantCulture), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is not null
            ? System.Convert.ChangeType(parameter, Nullable.GetUnderlyingType(targetType) ?? targetType, CultureInfo.InvariantCulture)
            : BindingOperations.DoNothing;
}
