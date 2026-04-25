namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// US0007/US0010: Result of a QR code scan (camera or USB) and server submission.
/// Contains the raw payload, validation outcome, and server response.
/// </summary>
public record ScanResult
{
    /// <summary>
    /// Raw QR code payload string.
    /// </summary>
    public string RawPayload { get; init; } = string.Empty;

    /// <summary>
    /// HMAC validation result (from IHmacValidator).
    /// </summary>
    public HmacValidationResult ValidationResult { get; init; } = null!;

    /// <summary>
    /// US0010: Server submission status.
    /// </summary>
    public ScanStatus Status { get; init; } = ScanStatus.Accepted;

    /// <summary>
    /// US0010: Unique scan ID from server (when accepted).
    /// </summary>
    public string? ScanId { get; init; }

    /// <summary>
    /// US0010: Student ID from server response.
    /// </summary>
    public string? StudentId { get; init; }

    /// <summary>
    /// Learner Reference Number (12-digit DepEd ID, optional).
    /// </summary>
    public string? Lrn { get; init; }

    /// <summary>
    /// US0010: Student full name.
    /// </summary>
    public string? StudentName { get; init; }

    /// <summary>
    /// US0010: Student grade level.
    /// </summary>
    public string? Grade { get; init; }

    /// <summary>
    /// US0010: Student section.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// US0091: Student program / strand (e.g., "STEM", "ABM", "REGULAR"). Null if not returned.
    /// </summary>
    public string? Program { get; init; }

    /// <summary>
    /// US0010: Scan type (ENTRY or EXIT).
    /// </summary>
    public string? ScanType { get; init; }

    /// <summary>
    /// Timestamp when the scan was captured.
    /// </summary>
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// US0010: Original scan ID for duplicate scans.
    /// </summary>
    public string? OriginalScanId { get; init; }

    /// <summary>
    /// US0010: Message from server (e.g., "Already scanned. Please proceed.").
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// US0010: Error reason code (e.g., "InvalidApiKey", "StudentInactive").
    /// </summary>
    public string? ErrorReason { get; init; }

    /// <summary>
    /// US0010: Retry-After period in seconds (for rate limiting).
    /// </summary>
    public int? RetryAfterSeconds { get; init; }

    /// <summary>
    /// True if this result is an optimistic local prediction, not yet confirmed by the server.
    /// The UI shows it immediately; ScanUpdated will fire with the real server result.
    /// </summary>
    public bool IsOptimistic { get; init; }

    /// <summary>
    /// US0076: Visitor pass code (e.g., "VISITOR-005"). Null for student scans.
    /// </summary>
    public string? PassCode { get; init; }

    /// <summary>
    /// US0076: Visitor pass number (e.g., 5). Null for student scans.
    /// </summary>
    public int? PassNumber { get; init; }

    /// <summary>
    /// US0076: True if this is a visitor scan (PassCode is present).
    /// </summary>
    public bool IsVisitorScan => PassCode != null;

    /// <summary>
    /// EP0011/US0090: Zero-based index of the camera that captured this scan. Null for USB scanner.
    /// </summary>
    public int? CameraIndex { get; init; }

    /// <summary>
    /// EP0011/US0090: User-assigned display name of the camera slot. Null for USB scanner.
    /// </summary>
    public string? CameraName { get; init; }

    /// <summary>
    /// True if the QR code passed HMAC validation.
    /// </summary>
    public bool IsValid => ValidationResult?.IsValid ?? false;
}
