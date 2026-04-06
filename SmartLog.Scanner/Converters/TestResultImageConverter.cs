using System.Globalization;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Converters;

/// <summary>
/// Converts ConnectionTestResult to an SVG icon filename.
/// Success → icon_check.svg, errors → icon_close.svg, None → ""
/// </summary>
public class TestResultImageConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is ConnectionTestResult result)
		{
			return result switch
			{
				ConnectionTestResult.Success => "icon_check.svg",
				ConnectionTestResult.None => string.Empty,
				_ => "icon_close.svg"
			};
		}

		return string.Empty;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
