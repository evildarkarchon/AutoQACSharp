using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AutoQAC.Converters;

/// <summary>
/// Returns true when the nullable bool value is exactly true (not null and not false).
/// Used to show the green checkmark indicator on valid path fields.
/// </summary>
public sealed class IsTrueConverter : IValueConverter
{
    public static IsTrueConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when the nullable bool value is exactly false (not null and not true).
/// Used to show the red X indicator on invalid path fields.
/// </summary>
public sealed class IsFalseConverter : IValueConverter
{
    public static IsFalseConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when the nullable bool value is not null (field has been touched/validated).
/// Used to show/hide path validation indicators.
/// </summary>
public sealed class IsNotNullConverter : IValueConverter
{
    public static IsNotNullConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
