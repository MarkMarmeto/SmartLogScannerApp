namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// US0006: Result of HMAC-SHA256 QR code validation.
/// Immutable record ensuring callers must check IsValid before accessing data.
/// </summary>
public record HmacValidationResult
{
    /// <summary>
    /// True if HMAC signature is valid and payload is well-formed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Extracted student ID (only populated for student QR codes when IsValid = true).
    /// </summary>
    public string? StudentId { get; init; }

    /// <summary>
    /// Extracted Unix timestamp string (only populated for student QR codes when IsValid = true).
    /// </summary>
    public string? Timestamp { get; init; }

    /// <summary>
    /// Extracted pass code (only populated for visitor QR codes when IsValid = true).
    /// </summary>
    public string? PassCode { get; init; }

    /// <summary>True when this result came from a visitor pass (SMARTLOG-V:) rather than a student QR.</summary>
    public bool IsVisitorScan => PassCode != null;

    /// <summary>
    /// Human-readable rejection reason (only populated when IsValid = false).
    /// Never includes HMAC secret or computed hash values for security.
    /// Types: "Malformed", "InvalidPrefix", "InvalidSignature", "InvalidBase64", "SecretUnavailable"
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// Creates a successful validation result for a student QR code.
    /// </summary>
    public static HmacValidationResult Success(string studentId, string timestamp) =>
        new() { IsValid = true, StudentId = studentId, Timestamp = timestamp };

    /// <summary>
    /// Creates a successful validation result for a visitor pass QR code.
    /// </summary>
    public static HmacValidationResult VisitorSuccess(string passCode) =>
        new() { IsValid = true, PassCode = passCode };

    /// <summary>
    /// Creates a failed validation result with rejection reason.
    /// </summary>
    public static HmacValidationResult Failure(string rejectionReason) =>
        new() { IsValid = false, RejectionReason = rejectionReason };
}
