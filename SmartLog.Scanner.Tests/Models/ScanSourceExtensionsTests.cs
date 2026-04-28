using SmartLog.Scanner.Core.Models;
using Xunit;

namespace SmartLog.Scanner.Tests.Models;

/// <summary>
/// EP0012/US0121: Locks the ScanSource → ScanMethod string mapping so a future enum
/// addition surfaces immediately as a failing test.
/// </summary>
public class ScanSourceExtensionsTests
{
    [Theory]
    [InlineData(ScanSource.Camera, "Camera")]
    [InlineData(ScanSource.UsbScanner, "USB")]
    public void ToScanMethodString_Maps_Source_Correctly(ScanSource source, string expected)
        => Assert.Equal(expected, source.ToScanMethodString());
}
