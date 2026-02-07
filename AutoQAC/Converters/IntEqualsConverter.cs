using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AutoQAC.Converters;

/// <summary>
/// Returns true when the integer value equals the integer ConverterParameter.
/// Used for showing/hiding controls based on ComboBox SelectedIndex.
/// </summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public static IntEqualsConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out var paramInt))
        {
            return intValue == paramInt;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
