using System.Globalization;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Converters;

/// <summary>
/// US0005: Converts ConnectionTestResult to visibility boolean.
/// None → false (hidden), any other value → true (visible)
/// </summary>
public class EnumNotNoneConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is ConnectionTestResult result && result != ConnectionTestResult.None;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
