using System.Globalization;

namespace SmartLog.Scanner.Converters;

/// <summary>
/// Converts the active filter string to a chip background color.
/// Returns primary color when the chip's filter matches the active filter, dimmed otherwise.
/// Usage: BackgroundColor="{Binding ActiveFilter, Converter={StaticResource FilterChipColorConverter}, ConverterParameter='Accepted'}"
/// </summary>
public class FilterChipColorConverter : IValueConverter
{
    private static readonly Color ActiveColor = Color.FromArgb("#2C5F5D");
    private static readonly Color InactiveColor = Color.FromArgb("#9E9E9E");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var activeFilter = value as string;
        var chipFilter = parameter as string;

        return string.Equals(activeFilter, chipFilter, StringComparison.OrdinalIgnoreCase)
            ? ActiveColor
            : InactiveColor;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
