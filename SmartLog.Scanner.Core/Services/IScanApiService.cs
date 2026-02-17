using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0010: Service for submitting validated scans to the SmartLog server API.
/// Handles ACCEPTED, DUPLICATE, REJECTED responses and offline fallback.
/// </summary>
public interface IScanApiService
{
    /// <summary>
    /// Submits a validated QR scan to the server.
    /// </summary>
    /// <param name="qrPayload">Complete QR code payload (SMARTLOG:...)</param>
    /// <param name="scannedAt">Timestamp when the QR was scanned</param>
    /// <param name="scanType">Scan direction: "ENTRY" or "EXIT"</param>
    /// <param name="cancellationToken">Cancellation token (default 10s timeout)</param>
    /// <returns>Scan result with server response or offline queued status</returns>
    Task<ScanResult> SubmitScanAsync(
        string qrPayload,
        DateTimeOffset scannedAt,
        string scanType,
        CancellationToken cancellationToken = default);
}
