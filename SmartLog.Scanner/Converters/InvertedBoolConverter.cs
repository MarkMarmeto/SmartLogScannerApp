using System.Globalization;

namespace SmartLog.Scanner.Converters;

/// <summary>
/// Converts a boolean to its inverse value.
/// Used for disabling buttons during save operations (IsSaving=true -> IsEnabled=false).
/// </summary>
public class InvertedBoolConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is bool b ? !b : false;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is bool b ? !b : false;
	}
}
