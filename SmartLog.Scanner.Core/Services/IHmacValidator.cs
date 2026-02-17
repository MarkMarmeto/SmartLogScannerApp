using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0006: Local HMAC-SHA256 QR code validation service.
/// Validates QR payloads in format: SMARTLOG:{studentId}:{timestamp}:{hmacBase64}
/// Uses constant-time comparison to prevent timing attacks.
/// </summary>
public interface IHmacValidator
{
    /// <summary>
    /// Validates a QR code payload's HMAC signature.
    /// </summary>
    /// <param name="qrPayload">Raw QR code string to validate</param>
    /// <returns>Validation result indicating success or typed failure reason</returns>
    Task<HmacValidationResult> ValidateAsync(string qrPayload);
}
