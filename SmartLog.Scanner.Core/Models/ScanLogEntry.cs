using System;

namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// Represents a single scan log entry for diagnostics and troubleshooting.
/// Captures all relevant information about a scan attempt.
/// </summary>
public class ScanLogEntry
{
    public int Id { get; set; }

    /// <summary>
    /// When the scan occurred
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Raw QR code payload that was scanned
    /// </summary>
    public string RawPayload { get; set; } = string.Empty;

    /// <summary>
    /// Student ID extracted from QR code (if valid)
    /// </summary>
    public string? StudentId { get; set; }

    /// <summary>
    /// Student name returned from server (if available)
    /// </summary>
    public string? StudentName { get; set; }

    /// <summary>
    /// Scan type (ENTRY/EXIT)
    /// </summary>
    public string ScanType { get; set; } = "ENTRY";

    /// <summary>
    /// Final status of the scan
    /// </summary>
    public ScanStatus Status { get; set; }

    /// <summary>
    /// Message from server or validation error
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Server scan ID (if successfully submitted)
    /// </summary>
    public string? ScanId { get; set; }

    /// <summary>
    /// Whether network was available when scan occurred
    /// </summary>
    public bool NetworkAvailable { get; set; }

    /// <summary>
    /// How long the scan took to process (milliseconds)
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// Grade and section (if available from server)
    /// </summary>
    public string? GradeSection { get; set; }

    /// <summary>
    /// Error details (if scan failed)
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// Scanning method used (Camera/USB)
    /// </summary>
    public string ScanMethod { get; set; } = "Unknown";

    /// <summary>
    /// Display-friendly status text with color
    /// </summary>
    public string StatusDisplay => Status switch
    {
        ScanStatus.Accepted => "✅ Accepted",
        ScanStatus.Duplicate => "⚠️ Duplicate",
        ScanStatus.Rejected => "❌ Rejected",
        ScanStatus.Queued => "💾 Queued (Offline)",
        ScanStatus.RateLimited => "⏱️ Rate Limited",
        ScanStatus.Error => "🚫 Error",
        ScanStatus.DebouncedLocally => "⚠️ Already Scanned",
        _ => "❓ Unknown"
    };

    /// <summary>
    /// Status color for UI display
    /// </summary>
    public string StatusColor => Status switch
    {
        ScanStatus.Accepted => "#4CAF50", // Green
        ScanStatus.Duplicate => "#FF9800", // Amber
        ScanStatus.Rejected => "#F44336", // Red
        ScanStatus.Queued => "#2196F3", // Blue
        ScanStatus.RateLimited => "#FFC107", // Yellow
        ScanStatus.Error => "#F44336", // Red
        ScanStatus.DebouncedLocally => "#9E9E9E", // Gray
        _ => "#607D8B" // Blue-gray
    };

    /// <summary>
    /// Formatted timestamp for display
    /// </summary>
    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("MMM dd, yyyy HH:mm:ss");

    /// <summary>
    /// True when there is technical detail worth showing (rejected / error scans).
    /// </summary>
    public bool HasErrorDetails =>
        !string.IsNullOrEmpty(ErrorDetails) &&
        (Status == ScanStatus.Rejected || Status == ScanStatus.Error);

    /// <summary>
    /// Human-readable processing time: "&lt;1s", "1.2s", etc.
    /// </summary>
    public string ProcessingTimeDisplay => ProcessingTimeMs switch
    {
        0 => "—",
        < 1000 => $"{ProcessingTimeMs}ms",
        _ => $"{ProcessingTimeMs / 1000.0:F1}s"
    };

    /// <summary>
    /// Short summary for list view
    /// </summary>
    public string Summary => $"{StudentName ?? StudentId ?? "Unknown"} - {StatusDisplay}";
}
