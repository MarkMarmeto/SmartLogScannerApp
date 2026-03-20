using System.Security.Cryptography;
using System.Text;
using Moq;
using SmartLog.Scanner.Core.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// US0006: Unit tests for HmacValidator - HMAC-SHA256 QR validation
/// </summary>
public class HmacValidatorTests
{
    private readonly Mock<ISecureConfigService> _mockSecureConfig;
    private readonly Mock<ILogger<HmacValidator>> _mockLogger;
    private readonly HmacValidator _validator;
    private const string TestSecret = "test-secret-key";

    public HmacValidatorTests()
    {
        _mockSecureConfig = new Mock<ISecureConfigService>();
        _mockLogger = new Mock<ILogger<HmacValidator>>();
        _validator = new HmacValidator(_mockSecureConfig.Object, null!, _mockLogger.Object);

        // Default: secret is available
        _mockSecureConfig.Setup(s => s.GetHmacSecretAsync())
            .ReturnsAsync(TestSecret);
    }

    #region AC1: QR Payload Parsing - Valid 4-Part Structure

    [Fact]
    public async Task ValidateAsync_EmptyPayload_ReturnsMalformed()
    {
        var result = await _validator.ValidateAsync("");

        Assert.False(result.IsValid);
        Assert.Contains("Malformed", result.RejectionReason);
    }

    [Fact]
    public async Task ValidateAsync_ThreeParts_ReturnsMalformedWithCount()
    {
        var result = await _validator.ValidateAsync("SMARTLOG:123:456");

        Assert.False(result.IsValid);
        Assert.Equal("Malformed: expected 4 colon-separated parts, found 3", result.RejectionReason);
    }

    [Fact]
    public async Task ValidateAsync_FiveParts_ReturnsMalformedWithCount()
    {
        var result = await _validator.ValidateAsync("SMARTLOG:123:456:abc:extra");

        Assert.False(result.IsValid);
        Assert.Equal("Malformed: expected 4 colon-separated parts, found 5", result.RejectionReason);
    }

    #endregion

    #region AC2: SMARTLOG Prefix Verification

    [Fact]
    public async Task ValidateAsync_LowercasePrefix_ReturnsInvalidPrefix()
    {
        var result = await _validator.ValidateAsync("smartlog:123:456:abc");

        Assert.False(result.IsValid);
        Assert.Equal("InvalidPrefix: expected 'SMARTLOG'", result.RejectionReason);
    }

    [Fact]
    public async Task ValidateAsync_MixedCasePrefix_ReturnsInvalidPrefix()
    {
        var result = await _validator.ValidateAsync("SmartLog:123:456:abc");

        Assert.False(result.IsValid);
        Assert.Equal("InvalidPrefix: expected 'SMARTLOG'", result.RejectionReason);
    }

    [Fact]
    public async Task ValidateAsync_WrongPrefix_ReturnsInvalidPrefix()
    {
        var result = await _validator.ValidateAsync("BADLOG:123:456:abc");

        Assert.False(result.IsValid);
        Assert.Equal("InvalidPrefix: expected 'SMARTLOG'", result.RejectionReason);
    }

    #endregion

    #region AC3: HMAC-SHA256 Computation

    [Fact]
    public async Task ValidateAsync_ValidHmac_ComputesOverStudentIdAndTimestamp()
    {
        // Arrange - compute expected HMAC with a recent timestamp (within 2-year expiry)
        var studentId = "12345";
        var timestamp = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds().ToString();
        var message = $"{studentId}:{timestamp}";
        var hmac = ComputeHmac(message, TestSecret);
        var payload = $"SMARTLOG:{studentId}:{timestamp}:{hmac}";

        // Act
        var result = await _validator.ValidateAsync(payload);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(studentId, result.StudentId);
        Assert.Equal(timestamp, result.Timestamp);
    }

    #endregion

    #region AC4: Constant-Time Signature Comparison

    [Fact]
    public async Task ValidateAsync_InvalidHmac_ReturnsInvalidSignature()
    {
        // Use wrong HMAC
        var payload = "SMARTLOG:123:456:aW52YWxpZC1obWFj"; // "invalid-hmac" in base64

        var result = await _validator.ValidateAsync(payload);

        Assert.False(result.IsValid);
        Assert.Equal("InvalidSignature: HMAC verification failed", result.RejectionReason);
    }

    [Fact]
    public async Task ValidateAsync_DifferentLengthHmac_ReturnsInvalidSignature()
    {
        // Use HMAC with different length (base64 of short string)
        var payload = "SMARTLOG:123:456:YWJj"; // "abc" in base64 (too short)

        var result = await _validator.ValidateAsync(payload);

        Assert.False(result.IsValid);
        Assert.Equal("InvalidSignature: HMAC verification failed", result.RejectionReason);
    }

    #endregion

    #region AC5: Valid QR Returns Success Result

    [Fact]
    public async Task ValidateAsync_ValidPayload_ReturnsSuccessWithData()
    {
        // Arrange - use a recent timestamp (within 2-year expiry)
        var studentId = "STU001";
        var timestamp = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds().ToString();
        var message = $"{studentId}:{timestamp}";
        var hmac = ComputeHmac(message, TestSecret);
        var payload = $"SMARTLOG:{studentId}:{timestamp}:{hmac}";

        // Act
        var result = await _validator.ValidateAsync(payload);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(studentId, result.StudentId);
        Assert.Equal(timestamp, result.Timestamp);
        Assert.Null(result.RejectionReason);
    }

    #endregion

    #region AC6: Invalid QR Returns Typed Rejection

    [Fact]
    public async Task ValidateAsync_InvalidBase64_ReturnsInvalidBase64()
    {
        var payload = "SMARTLOG:123:456:not-valid-base64!!!";

        var result = await _validator.ValidateAsync(payload);

        Assert.False(result.IsValid);
        Assert.Contains("InvalidBase64", result.RejectionReason);
        Assert.Null(result.StudentId);
        Assert.Null(result.Timestamp);
    }

    [Fact]
    public async Task ValidateAsync_RejectionReasonNeverIncludesSecret()
    {
        // Test all rejection paths
        var testCases = new[]
        {
            "SMARTLOG:123:456", // Malformed
            "BADLOG:123:456:abc", // InvalidPrefix
            "SMARTLOG:123:456:aW52YWxpZA==", // InvalidSignature
            "SMARTLOG:123:456:not-base64!!!" // InvalidBase64
        };

        foreach (var testCase in testCases)
        {
            var result = await _validator.ValidateAsync(testCase);

            Assert.False(result.IsValid);
            Assert.NotNull(result.RejectionReason);
            Assert.DoesNotContain(TestSecret, result.RejectionReason);
        }
    }

    #endregion

    #region AC8: HMAC Secret Retrieval from SecureStorage

    [Fact]
    public async Task ValidateAsync_SecretNotConfigured_ReturnsSecretUnavailable()
    {
        // Arrange - secret is null
        _mockSecureConfig.Setup(s => s.GetHmacSecretAsync())
            .ReturnsAsync((string?)null);

        var payload = "SMARTLOG:123:456:YWJjZGVm";

        // Act
        var result = await _validator.ValidateAsync(payload);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("SecretUnavailable", result.RejectionReason);
    }

    [Fact]
    public async Task ValidateAsync_SecretEmpty_ReturnsSecretUnavailable()
    {
        // Arrange - secret is empty string
        _mockSecureConfig.Setup(s => s.GetHmacSecretAsync())
            .ReturnsAsync(string.Empty);

        var payload = "SMARTLOG:123:456:YWJjZGVm";

        // Act
        var result = await _validator.ValidateAsync(payload);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("SecretUnavailable", result.RejectionReason);
    }

    [Fact]
    public async Task ValidateAsync_SecretRetrieved_CallsSecureConfigService()
    {
        // Arrange - use a recent timestamp (within 2-year expiry)
        var studentId = "123";
        var timestamp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds().ToString();
        var message = $"{studentId}:{timestamp}";
        var hmac = ComputeHmac(message, TestSecret);
        var payload = $"SMARTLOG:{studentId}:{timestamp}:{hmac}";

        // Act
        await _validator.ValidateAsync(payload);

        // Assert
        _mockSecureConfig.Verify(s => s.GetHmacSecretAsync(), Times.Once);
    }

    #endregion

    #region Helper Methods

    private static string ComputeHmac(string message, string secret)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var messageBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(messageBytes);
        return Convert.ToBase64String(hash);
    }

    #endregion
}
