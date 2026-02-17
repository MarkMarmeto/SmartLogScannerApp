using System.Globalization;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Converters;

/// <summary>
/// US0005: Converts ConnectionTestResult to icon string.
/// Success → "✓", errors → "✗", None → ""
/// </summary>
public class TestResultIconConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is ConnectionTestResult result)
		{
			return result switch
			{
				ConnectionTestResult.Success => "✓",
				ConnectionTestResult.None => string.Empty,
				_ => "✗"
			};
		}

		return string.Empty;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
