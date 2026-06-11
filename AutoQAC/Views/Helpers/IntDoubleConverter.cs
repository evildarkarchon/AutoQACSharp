using System;
using Microsoft.UI.Xaml.Data;

namespace AutoQAC.Views.Helpers;

public sealed class IntDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int intValue ? (double)intValue : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is double doubleValue ? (int)Math.Round(doubleValue) : 0;
}
