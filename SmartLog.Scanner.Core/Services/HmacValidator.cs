using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0006: HMAC-SHA256 QR code validator using constant-time comparison.
/// Validates student format: SMARTLOG:{studentId}:{timestamp}:{hmacBase64}
/// Validates visitor format: SMARTLOG-V:{passCode}:{timestamp}:{hmacBase64}
/// </summary>
public class HmacValidator : IHmacValidator
{
    private readonly ISecureConfigService _secureConfig;
    private readonly ILogger<HmacValidator> _logger;

    public HmacValidator(
        ISecureConfigService secureConfig,
        ILogger<HmacValidator> logger)
    {
        _secureConfig = secureConfig;
        _logger = logger;
    }

    public async Task<HmacValidationResult> ValidateAsync(string qrPayload)
    {
        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            return HmacValidationResult.Failure("Malformed: QR payload is empty");
        }

        var parts = qrPayload.Split(':');
        var prefix = parts[0];

        // Visitor Pass: SMARTLOG-V:{passCode}:{timestamp}:{hmac}
        if (prefix == "SMARTLOG-V")
        {
            if (parts.Length != 4)
            {
                return HmacValidationResult.Failure(
                    $"Malformed: visitor pass expected 4 colon-separated parts, found {parts.Length}");
            }
            return await ValidateVisitorAsync(parts[1], parts[2], parts[3]);
        }

        // Student QR: SMARTLOG:{studentId}:{timestamp}:{hmac}
        if (parts.Length != 4)
        {
            return HmacValidationResult.Failure(
                $"Malformed: expected 4 colon-separated parts, found {parts.Length}");
        }

        if (prefix != "SMARTLOG")
        {
            return HmacValidationResult.Failure("InvalidPrefix: expected 'SMARTLOG'");
        }

        var studentId = parts[1];
        var timestamp = parts[2];
        var hmacBase64 = parts[3];

        // SECURITY FIX (CRITICAL-01): Retrieve HMAC secret from SecureStorage ONLY
        // Secrets are never stored in file config for security reasons
        string? secret = await _secureConfig.GetHmacSecretAsync();

        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogError("HMAC secret not configured in SecureStorage");
            return HmacValidationResult.Failure(
                "SecretUnavailable: HMAC secret not configured. Please run device setup.");
        }

        secret = secret.Trim();

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

        // Compute HMAC-SHA256 over "{studentId}:{timestamp}"
        var message = $"{studentId}:{timestamp}";
        var secretBytes = Encoding.UTF8.GetBytes(secret);

        byte[] computedHmac;
        using (var hmac = new HMACSHA256(secretBytes))
        {
            computedHmac = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        }

        if (!CryptographicOperations.FixedTimeEquals(computedHmac, payloadHmac))
        {
            _logger.LogWarning("Invalid HMAC signature for payload (StudentId: {StudentId})", studentId);
            return HmacValidationResult.Failure("InvalidSignature: HMAC verification failed");
        }

        if (long.TryParse(timestamp, out var unixTimestamp))
        {
            var qrIssuedAt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
            if (DateTimeOffset.UtcNow - qrIssuedAt > TimeSpan.FromDays(730))
            {
                _logger.LogWarning("Expired QR code - StudentId: {StudentId}, IssuedAt: {IssuedAt}",
                    studentId, qrIssuedAt);
                return HmacValidationResult.Failure(
                    "Expired: QR code has expired. Student needs a new ID card.");
            }
        }

        _logger.LogInformation("Valid QR code - StudentId: {StudentId}, Timestamp: {Timestamp}",
            studentId, timestamp);
        return HmacValidationResult.Success(studentId, timestamp);
    }

    private async Task<HmacValidationResult> ValidateVisitorAsync(string passCode, string timestamp, string hmacBase64)
    {
        string? secret = await _secureConfig.GetHmacSecretAsync();

        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogError("HMAC secret not configured in SecureStorage");
            return HmacValidationResult.Failure(
                "SecretUnavailable: HMAC secret not configured. Please run device setup.");
        }

        secret = secret.Trim();

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

        // Visitor pass HMAC signs "{passCode}:{timestamp}" — mirrors VisitorPassService.ComputeHmac
        var message = $"{passCode}:{timestamp}";
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        byte[] computedHmac;
        using (var hmac = new HMACSHA256(secretBytes))
        {
            computedHmac = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        }

        if (!CryptographicOperations.FixedTimeEquals(computedHmac, payloadHmac))
        {
            _logger.LogWarning("Invalid HMAC signature for visitor pass (PassCode: {PassCode})", passCode);
            return HmacValidationResult.Failure("InvalidSignature: HMAC verification failed");
        }

        _logger.LogInformation("Valid visitor pass - PassCode: {PassCode}", passCode);
        return HmacValidationResult.VisitorSuccess(passCode);
    }
}
