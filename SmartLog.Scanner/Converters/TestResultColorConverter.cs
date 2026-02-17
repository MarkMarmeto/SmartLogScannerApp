using System.Globalization;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Converters;

/// <summary>
/// US0005: Converts ConnectionTestResult to color for text display.
/// Success → Green, errors → Red, None → Gray
/// </summary>
public class TestResultColorConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is ConnectionTestResult result)
		{
			return result switch
			{
				ConnectionTestResult.Success => Colors.Green,
				ConnectionTestResult.None => Colors.Gray,
				_ => Colors.Red
			};
		}

		return Colors.Gray;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
