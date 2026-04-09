using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0007/US0008: QR code scanner service interface.
/// Abstracts camera (US0007) and USB keyboard wedge (US0008) implementations.
/// </summary>
public interface IQrScannerService
{
    /// <summary>
    /// Fired when a QR code is detected and locally validated (may be optimistic).
    /// </summary>
    event EventHandler<ScanResult>? ScanCompleted;

    /// <summary>
    /// Fired after the server confirms or corrects an optimistic ScanCompleted result.
    /// Only raised for camera scans submitted in optimistic mode.
    /// </summary>
    event EventHandler<ScanResult>? ScanUpdated;

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
