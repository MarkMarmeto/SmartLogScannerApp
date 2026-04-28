namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// EP0012/US0121: Maps ScanSource to the string stored in ScanLogEntry.ScanMethod.
/// Kept in Core so tests can call it directly without referencing the MAUI project.
/// </summary>
public static class ScanSourceExtensions
{
    public static string ToScanMethodString(this ScanSource source) => source switch
    {
        ScanSource.Camera => "Camera",
        ScanSource.UsbScanner => "USB",
        _ => "Unknown"
    };
}
