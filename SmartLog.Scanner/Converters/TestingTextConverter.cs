using System.Globalization;

namespace SmartLog.Scanner.Converters;

/// <summary>
/// US0005: Converts IsTestingConnection boolean to button text.
/// true → "Testing...", false → "Test Connection"
/// </summary>
public class TestingTextConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is bool isTesting && isTesting ? "Testing..." : "Test Connection";
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
