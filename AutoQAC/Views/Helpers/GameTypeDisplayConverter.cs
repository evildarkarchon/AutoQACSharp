using System;
using AutoQAC.Models;
using Microsoft.UI.Xaml.Data;

namespace AutoQAC.Views.Helpers;

public sealed class GameTypeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is GameType gameType ? BindingHelpers.GameTypeDisplayName(gameType) : "Unknown";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
