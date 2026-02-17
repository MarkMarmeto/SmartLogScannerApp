using Microsoft.Extensions.Logging;
using Moq;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;
using Xunit;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// US0008: Tests for USB keyboard wedge QR scanner service.
/// </summary>
public class UsbQrScannerServiceTests
{
    private readonly Mock<IHmacValidator> _hmacValidatorMock;
    private readonly Mock<ILogger<UsbQrScannerService>> _loggerMock;
    private readonly UsbQrScannerService _service;

    public UsbQrScannerServiceTests()
    {
        _hmacValidatorMock = new Mock<IHmacValidator>();
        _loggerMock = new Mock<ILogger<UsbQrScannerService>>();
        _service = new UsbQrScannerService(_hmacValidatorMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task StartAsync_ShouldSetIsScanningToTrue()
    {
        // Act
        await _service.StartAsync();

        // Assert
        Assert.True(_service.IsScanning);
    }

    [Fact]
    public async Task StopAsync_ShouldSetIsScanningToFalse()
    {
        // Arrange
        await _service.StartAsync();

        // Act
        await _service.StopAsync();

        // Assert
        Assert.False(_service.IsScanning);
    }

    [Fact]
    public async Task ProcessKeystroke_WhenNotScanning_ShouldIgnoreInput()
    {
        // Arrange
        ScanResult? capturedResult = null;
        _service.ScanCompleted += (sender, result) => capturedResult = result;

        // Act
        _service.ProcessKeystroke("S");
        await Task.Delay(150); // Wait for timeout

        // Assert
        Assert.Null(capturedResult);
    }

    [Fact]
    public async Task ProcessKeystroke_RapidKeystrokes_ShouldBufferCharacters()
    {
        // Arrange
        await _service.StartAsync();
        var validationResult = HmacValidationResult.Success("STU12345", "1234567890");
        _hmacValidatorMock.Setup(x => x.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(validationResult);

        ScanResult? capturedResult = null;
        _service.ScanCompleted += (sender, result) => capturedResult = result;

        // Act - Simulate rapid keystrokes from scanner (< 100ms apart)
        _service.ProcessKeystroke("S");
        await Task.Delay(10);
        _service.ProcessKeystroke("M");
        await Task.Delay(10);
        _service.ProcessKeystroke("A");
        await Task.Delay(10);
        _service.ProcessKeystroke("R");
        await Task.Delay(10);
        _service.ProcessKeystroke("T");
        await Task.Delay(10);
        _service.ProcessKeystroke("L");
        await Task.Delay(10);
        _service.ProcessKeystroke("O");
        await Task.Delay(10);
        _service.ProcessKeystroke("G");
        await Task.Delay(10);
        _service.ProcessKeystroke(":");

        // Wait for timeout to complete processing
        await Task.Delay(150);

        // Assert
        Assert.NotNull(capturedResult);
        Assert.Contains("SMARTLOG:", capturedResult.RawPayload);
    }

    [Fact]
    public async Task ProcessKeystroke_SlowKeystrokes_ShouldDiscardPreviousBuffer()
    {
        // Arrange
        await _service.StartAsync();
        ScanResult? capturedResult = null;
        _service.ScanCompleted += (sender, result) => capturedResult = result;

        // Act - Simulate slow typing (> 100ms apart)
        _service.ProcessKeystroke("S");
        await Task.Delay(150); // Slow keystroke - should discard "S"
        _service.ProcessKeystroke("M");
        await Task.Delay(150); // Wait for timeout

        // Assert - No valid SMARTLOG pattern, so no event should fire
        Assert.Null(capturedResult);
    }

    [Fact]
    public async Task ProcessEnterKey_WithBufferedInput_ShouldProcessImmediately()
    {
        // Arrange
        await _service.StartAsync();
        var validPayload = "SMARTLOG:STU12345:1234567890:dGVzdC1obWFj";
        var validationResult = HmacValidationResult.Success("STU12345", "1234567890");
        _hmacValidatorMock.Setup(x => x.ValidateAsync(validPayload))
            .ReturnsAsync(validationResult);

        ScanResult? capturedResult = null;
        _service.ScanCompleted += (sender, result) => capturedResult = result;

        // Act - Simulate rapid keystrokes followed by Enter
        foreach (var ch in validPayload)
        {
            _service.ProcessKeystroke(ch.ToString());
            await Task.Delay(10);
        }
        _service.ProcessEnterKey();

        // Wait a bit for async processing
        await Task.Delay(50);

        // Assert
        Assert.NotNull(capturedResult);
        Assert.Equal(validPayload, capturedResult.RawPayload);
        Assert.True(capturedResult.IsValid);
        Assert.Equal("STU12345", capturedResult.StudentId);
    }

    [Fact]
    public async Task ProcessEnterKey_WithEmptyBuffer_ShouldDoNothing()
    {
        // Arrange
        await _service.StartAsync();
        ScanResult? capturedResult = null;
        _service.ScanCompleted += (sender, result) => capturedResult = result;

        // Act
        _service.ProcessEnterKey();
        await Task.Delay(50);

        // Assert
        Assert.Null(capturedResult);
    }

    [Fact]
    public async Task Timeout_WithValidSmartlogPattern_ShouldProcessPayload()
    {
        // Arrange
        await _service.StartAsync();
        var validPayload = "SMARTLOG:STU12345:1234567890:dGVzdC1obWFj";
        var validationResult = HmacValidationResult.Success("STU12345", "1234567890");
        _hmacValidatorMock.Setup(x => x.ValidateAsync(validPayload))
            .ReturnsAsync(validationResult);

        ScanResult? capturedResult = null;
        _service.ScanCompleted += (sender, result) => capturedResult = result;

        // Act - Rapid keystrokes to build SMARTLOG pattern
        foreach (var ch in validPayload)
        {
            _service.ProcessKeystroke(ch.ToString());
            await Task.Delay(10);
        }

        // Wait for 100ms timeout to trigger
        await Task.Delay(150);

        // Assert
        Assert.NotNull(capturedResult);
        Assert.Equal(validPayload, capturedResult.RawPayload);
        Assert.True(capturedResult.IsValid);
    }

    [Fact]
    public async Task Timeout_WithoutSmartlogPattern_ShouldDiscardPayload()
    {
        // Arrange
        await _service.StartAsync();
        ScanResult? capturedResult = null;
        _service.ScanCompleted += (sender, result) => capturedResult = result;

        // Act - Rapid keystrokes but no SMARTLOG prefix
        var invalidPayload = "NOTVALID:12345";
        foreach (var ch in invalidPayload)
        {
            _service.ProcessKeystroke(ch.ToString());
            await Task.Delay(10);
        }

        // Wait for timeout
        await Task.Delay(150);

        // Assert - Should be discarded, no event fired
        Assert.Null(capturedResult);
    }

    [Fact]
    public async Task ProcessQrCodeAsync_WithValidPayload_ShouldRaiseScanCompletedEvent()
    {
        // Arrange
        await _service.StartAsync();
        var validPayload = "SMARTLOG:STU12345:1234567890:dGVzdC1obWFj";
        var validationResult = HmacValidationResult.Success("STU12345", "1234567890");
        _hmacValidatorMock.Setup(x => x.ValidateAsync(validPayload))
            .ReturnsAsync(validationResult);

        ScanResult? capturedResult = null;
        _service.ScanCompleted += (sender, result) => capturedResult = result;

        // Act
        await _service.ProcessQrCodeAsync(validPayload);

        // Assert
        Assert.NotNull(capturedResult);
        Assert.Equal(validPayload, capturedResult.RawPayload);
        Assert.True(capturedResult.IsValid);
        Assert.Equal("STU12345", capturedResult.StudentId);
    }

    [Fact]
    public async Task ProcessQrCodeAsync_WithInvalidPayload_ShouldRaiseScanCompletedEventWithFailure()
    {
        // Arrange
        await _service.StartAsync();
        var invalidPayload = "SMARTLOG:STU99999:1234567890:aW52YWxpZC1obWFj";
        var validationResult = HmacValidationResult.Failure("Invalid HMAC signature");
        _hmacValidatorMock.Setup(x => x.ValidateAsync(invalidPayload))
            .ReturnsAsync(validationResult);

        ScanResult? capturedResult = null;
        _service.ScanCompleted += (sender, result) => capturedResult = result;

        // Act
        await _service.ProcessQrCodeAsync(invalidPayload);

        // Assert
        Assert.NotNull(capturedResult);
        Assert.Equal(invalidPayload, capturedResult.RawPayload);
        Assert.False(capturedResult.IsValid);
    }

    [Fact]
    public async Task MultipleScans_ShouldEachRaiseSeparateEvents()
    {
        // Arrange
        await _service.StartAsync();
        var validationResult1 = HmacValidationResult.Success("STU11111", "1111111111");
        var validationResult2 = HmacValidationResult.Success("STU22222", "2222222222");

        _hmacValidatorMock.Setup(x => x.ValidateAsync(It.Is<string>(s => s.Contains("STU11111"))))
            .ReturnsAsync(validationResult1);
        _hmacValidatorMock.Setup(x => x.ValidateAsync(It.Is<string>(s => s.Contains("STU22222"))))
            .ReturnsAsync(validationResult2);

        var capturedResults = new List<ScanResult>();
        _service.ScanCompleted += (sender, result) => capturedResults.Add(result);

        // Act - First scan
        await _service.ProcessQrCodeAsync("SMARTLOG:STU11111:1111111111:aGFzaDE=");
        await Task.Delay(50);

        // Act - Second scan
        await _service.ProcessQrCodeAsync("SMARTLOG:STU22222:2222222222:aGFzaDI=");
        await Task.Delay(50);

        // Assert
        Assert.Equal(2, capturedResults.Count);
        Assert.Equal("STU11111", capturedResults[0].StudentId);
        Assert.Equal("STU22222", capturedResults[1].StudentId);
    }
}
