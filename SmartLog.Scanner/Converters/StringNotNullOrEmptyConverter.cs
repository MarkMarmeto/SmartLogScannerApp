using System.Globalization;

namespace SmartLog.Scanner.Converters;

/// <summary>
/// Converts a string to a boolean indicating if the string is not null or empty.
/// Used for showing/hiding error messages in the UI.
/// </summary>
public class StringNotNullOrEmptyConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return !string.IsNullOrEmpty(value as string);
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
