using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Garethp.ModsOfMistriaGUI.Converters;

/// <summary>
/// MenuItem can retain a layout slot after IsVisible is changed. Give optional menu entries an
/// explicit zero height while they are unavailable so the menu never presents an unexplained gap.
/// </summary>
public sealed class BooleanToMenuHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? double.NaN : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}
