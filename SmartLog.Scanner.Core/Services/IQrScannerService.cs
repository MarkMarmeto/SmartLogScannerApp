using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0007/US0008: QR code scanner service interface.
/// Abstracts camera (US0007) and USB keyboard wedge (US0008) implementations.
/// </summary>
public interface IQrScannerService
{
    /// <summary>
    /// Fired when a QR code is successfully scanned and validated.
    /// </summary>
    event EventHandler<ScanResult>? ScanCompleted;

    /// <summary>
    /// Starts the scanner (camera or USB listener).
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stops the scanner and releases resources.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// True if the scanner is currently active.
    /// </summary>
    bool IsScanning { get; }
}
