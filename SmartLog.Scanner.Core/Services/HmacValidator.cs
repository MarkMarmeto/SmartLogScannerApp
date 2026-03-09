using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0006: HMAC-SHA256 QR code validator using constant-time comparison.
/// Validates format: SMARTLOG:{studentId}:{timestamp}:{hmacBase64}
/// </summary>
public class HmacValidator : IHmacValidator
{
    private readonly ISecureConfigService _secureConfig;
    private readonly FileConfigService _fileConfig;
    private readonly ILogger<HmacValidator> _logger;

    public HmacValidator(
        ISecureConfigService secureConfig,
        FileConfigService fileConfig,
        ILogger<HmacValidator> logger)
    {
        _secureConfig = secureConfig;
        _fileConfig = fileConfig;
        _logger = logger;
    }

    public async Task<HmacValidationResult> ValidateAsync(string qrPayload)
    {
        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            return HmacValidationResult.Failure("Malformed: QR payload is empty");
        }

        // AC1: Split on ":" expecting exactly 4 parts
        var parts = qrPayload.Split(':');
        if (parts.Length != 4)
        {
            return HmacValidationResult.Failure(
                $"Malformed: expected 4 colon-separated parts, found {parts.Length}");
        }

        var prefix = parts[0];
        var studentId = parts[1];
        var timestamp = parts[2];
        var hmacBase64 = parts[3];

        // AC2: Verify SMARTLOG prefix (case-sensitive)
        if (prefix != "SMARTLOG")
        {
            return HmacValidationResult.Failure("InvalidPrefix: expected 'SMARTLOG'");
        }

        // SECURITY FIX (CRITICAL-01): Retrieve HMAC secret from SecureStorage ONLY
        // Secrets are no longer stored in file config for security reasons
        string? secret = await _secureConfig.GetHmacSecretAsync();

        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogError("HMAC secret not configured in SecureStorage");
            return HmacValidationResult.Failure(
                "SecretUnavailable: HMAC secret not configured. Please run device setup.");
        }

        // Decode base64 HMAC from payload
        byte[] payloadHmac;
        try
        {
            payloadHmac = Convert.FromBase64String(hmacBase64);
        }
        catch (FormatException)
        {
            return HmacValidationResult.Failure(
                "InvalidBase64: HMAC signature is not valid base64");
        }

        // AC3: Compute HMAC-SHA256 over "{studentId}:{timestamp}"
        var message = $"{studentId}:{timestamp}";
        var secretBytes = Encoding.UTF8.GetBytes(secret);

        byte[] computedHmac;
        using (var hmac = new HMACSHA256(secretBytes))
        {
            computedHmac = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        }

        // AC4: Constant-time comparison using CryptographicOperations.FixedTimeEquals
        bool isValid = CryptographicOperations.FixedTimeEquals(computedHmac, payloadHmac);

        if (!isValid)
        {
            _logger.LogWarning("Invalid HMAC signature for payload (StudentId: {StudentId})", studentId);
            return HmacValidationResult.Failure("InvalidSignature: HMAC verification failed");
        }

        // AC5: Return success with parsed data
        _logger.LogInformation("Valid QR code - StudentId: {StudentId}, Timestamp: {Timestamp}",
            studentId, timestamp);
        return HmacValidationResult.Success(studentId, timestamp);
    }
}
