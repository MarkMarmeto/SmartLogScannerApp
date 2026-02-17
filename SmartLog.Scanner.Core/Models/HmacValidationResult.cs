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
    /// Extracted student ID (only populated when IsValid = true).
    /// </summary>
    public string? StudentId { get; init; }

    /// <summary>
    /// Extracted Unix timestamp string (only populated when IsValid = true).
    /// </summary>
    public string? Timestamp { get; init; }

    /// <summary>
    /// Human-readable rejection reason (only populated when IsValid = false).
    /// Never includes HMAC secret or computed hash values for security.
    /// Types: "Malformed", "InvalidPrefix", "InvalidSignature", "InvalidBase64", "SecretUnavailable"
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static HmacValidationResult Success(string studentId, string timestamp) =>
        new() { IsValid = true, StudentId = studentId, Timestamp = timestamp };

    /// <summary>
    /// Creates a failed validation result with rejection reason.
    /// </summary>
    public static HmacValidationResult Failure(string rejectionReason) =>
        new() { IsValid = false, RejectionReason = rejectionReason };
}
