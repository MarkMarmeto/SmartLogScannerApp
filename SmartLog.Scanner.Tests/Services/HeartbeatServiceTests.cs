using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;
using Xunit;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// US0120: Unit tests for HeartbeatService.
/// </summary>
public class HeartbeatServiceTests
{
    private readonly Mock<IHttpClientFactory> _mockFactory;
    private readonly IConfiguration _config;
    private readonly Mock<IPreferencesService> _mockPreferences;
    private readonly Mock<ISecureConfigService> _mockSecureConfig;
    private readonly Mock<IOfflineQueueService> _mockOfflineQueue;
    private readonly Mock<IScanHistoryService> _mockScanHistory;
    private readonly Mock<IQrScannerService> _mockUsbScanner;
    private readonly Mock<IHealthCheckService> _mockHealthCheck;
    private readonly Mock<ILogger<HeartbeatService>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHandler;

    public HeartbeatServiceTests()
    {
        _mockFactory = new Mock<IHttpClientFactory>();
        _mockPreferences = new Mock<IPreferencesService>();
        _mockSecureConfig = new Mock<ISecureConfigService>();
        _mockOfflineQueue = new Mock<IOfflineQueueService>();
        _mockScanHistory = new Mock<IScanHistoryService>();
        _mockUsbScanner = new Mock<IQrScannerService>();
        _mockHealthCheck = new Mock<IHealthCheckService>();
        _mockLogger = new Mock<ILogger<HeartbeatService>>();
        _mockHandler = new Mock<HttpMessageHandler>();

        // Use a real IConfiguration with in-memory defaults (GetValue<T> is an extension method)
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Heartbeat:BaseIntervalSeconds"] = "60",
                ["Heartbeat:MaxBackoffSeconds"] = "300",
                ["Heartbeat:RequestTimeoutSeconds"] = "10"
            })
            .Build();

        // Default: use factory client (no self-signed cert override)
        _mockPreferences.Setup(p => p.GetAcceptSelfSignedCerts()).Returns(false);

        // Default: online — tests that need offline override this explicitly
        _mockHealthCheck.SetupGet(h => h.IsOnline).Returns(true);

        var httpClient = new HttpClient(_mockHandler.Object);
        _mockFactory.Setup(f => f.CreateClient("Heartbeat")).Returns(httpClient);

        // Default scan history returns empty list
        _mockScanHistory.Setup(s => s.GetRecentLogsAsync(1))
            .ReturnsAsync(new List<ScanLogEntry>());

        // Default queue count
        _mockOfflineQueue.Setup(q => q.GetQueueCountAsync())
            .ReturnsAsync(0);
    }

    private HeartbeatService BuildService() => new(
        _mockFactory.Object,
        _config,
        _mockPreferences.Object,
        _mockSecureConfig.Object,
        _mockOfflineQueue.Object,
        _mockScanHistory.Object,
        _mockUsbScanner.Object,
        _mockHealthCheck.Object,
        _mockLogger.Object);

    // ── ComputeNextInterval ────────────────────────────────────────────────────

    [Theory]
    [InlineData(60, true, 60, 300, 60)]   // success → resets to base
    [InlineData(60, false, 60, 300, 120)] // failure from base → 2×
    [InlineData(120, false, 60, 300, 240)] // failure → 2×
    [InlineData(240, false, 60, 300, 300)] // failure → 2× but capped at max
    [InlineData(300, false, 60, 300, 300)] // failure at cap → stays at cap
    public void ComputeNextInterval_ReturnsExpected(
        int current, bool success, int baseSeconds, int maxSeconds, int expected)
    {
        var result = HeartbeatService.ComputeNextInterval(current, success, baseSeconds, maxSeconds);
        Assert.Equal(expected, result);
    }

    // ── SendHeartbeatAsync — US0131: skip when offline ────────────────────────

    [Fact]
    public async Task SendHeartbeatAsync_WhenOffline_ReturnsFalseWithoutPost()
    {
        _mockHealthCheck.SetupGet(h => h.IsOnline).Returns(false);
        var service = BuildService();

        var result = await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.False(result);
        _mockHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendHeartbeatAsync_WhenOnline_DoesPost()
    {
        _mockHealthCheck.SetupGet(h => h.IsOnline).Returns(true);
        SetupSuccessfulRequest();
        var service = BuildService();

        var result = await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.True(result);
        _mockHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendHeartbeatAsync_WhenIsOnlineNull_DoesPost()
    {
        // null = startup state — heartbeat should still fire
        _mockHealthCheck.SetupGet(h => h.IsOnline).Returns((bool?)null);
        SetupSuccessfulRequest();
        var service = BuildService();

        var result = await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.True(result);
        _mockHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // ── SendHeartbeatAsync — no-op cases ──────────────────────────────────────

    [Fact]
    public async Task SendHeartbeatAsync_EmptyApiKey_ReturnsTrueWithoutPost()
    {
        _mockSecureConfig.Setup(s => s.GetApiKeyAsync()).ReturnsAsync((string?)null);
        var service = BuildService();

        var result = await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.True(result);
        _mockHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendHeartbeatAsync_EmptyServerUrl_ReturnsTrueWithoutPost()
    {
        _mockSecureConfig.Setup(s => s.GetApiKeyAsync()).ReturnsAsync("sk_live_test");
        _mockPreferences.Setup(p => p.GetServerBaseUrl()).Returns(string.Empty);
        var service = BuildService();

        var result = await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.True(result);
        _mockHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    // ── SendHeartbeatAsync — happy path ───────────────────────────────────────

    [Fact]
    public async Task SendHeartbeatAsync_204Response_ReturnsTrue()
    {
        SetupSuccessfulRequest();
        var service = BuildService();

        var result = await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task SendHeartbeatAsync_IncludesApiKeyHeader()
    {
        const string expectedKey = "sk_live_abc123";
        SetupSuccessfulRequest(apiKey: expectedKey);

        HttpRequestMessage? capturedRequest = null;
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = BuildService();
        await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.Headers.Contains("X-API-Key"));
        Assert.Equal(expectedKey, capturedRequest.Headers.GetValues("X-API-Key").First());
    }

    // ── SendHeartbeatAsync — failure cases ────────────────────────────────────

    [Fact]
    public async Task SendHeartbeatAsync_401Response_ReturnsFalse()
    {
        SetupCredentials();
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var service = BuildService();
        var result = await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task SendHeartbeatAsync_HttpRequestException_ReturnsFalse()
    {
        SetupCredentials();
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var service = BuildService();
        var result = await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task SendHeartbeatAsync_Timeout_ReturnsFalse()
    {
        SetupCredentials();
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("timed out"));

        var service = BuildService();
        var result = await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.False(result);
    }

    // ── BuildPayloadAsync — via SendHeartbeatAsync inspection ────────────────

    [Fact]
    public async Task SendHeartbeatAsync_QueuedScansCount_ReflectsOfflineQueueService()
    {
        const int expectedCount = 7;
        SetupSuccessfulRequest();
        _mockOfflineQueue.Setup(q => q.GetQueueCountAsync()).ReturnsAsync(expectedCount);

        string? capturedBody = null;
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = BuildService();
        await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains($"\"queuedScansCount\":{expectedCount}", capturedBody);
    }

    [Fact]
    public async Task SendHeartbeatAsync_LastScanAt_ReflectsMostRecentLog()
    {
        var expectedTimestamp = new DateTimeOffset(2026, 4, 28, 8, 30, 0, TimeSpan.Zero);
        SetupSuccessfulRequest();
        _mockScanHistory.Setup(s => s.GetRecentLogsAsync(1))
            .ReturnsAsync(new List<ScanLogEntry>
            {
                new() { Timestamp = expectedTimestamp }
            });

        string? capturedBody = null;
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = BuildService();
        await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("lastScanAt", capturedBody);
        Assert.Contains("2026-04-28", capturedBody);
    }

    // ── StartAsync lifecycle ──────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_CalledTwice_SecondCallIsNoOp()
    {
        var service = BuildService();

        await service.StartAsync();
        await service.StartAsync(); // should log warning and return

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("already started")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        await service.StopAsync();
    }

    [Fact]
    public async Task StopAsync_CancelsLoopWithoutException()
    {
        var service = BuildService();
        await service.StartAsync();

        // Allow loop to start
        await Task.Delay(50);

        var exception = await Record.ExceptionAsync(() => service.StopAsync());
        Assert.Null(exception);
    }

    // ── EP0012/US0121: USB age field ─────────────────────────────────────────

    [Fact]
    public async Task Payload_UsbScannerLastScanAgeSeconds_Null_Before_Any_Usb_Scan()
    {
        SetupSuccessfulRequest();
        string? capturedBody = null;
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = BuildService();
        await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"usbScannerLastScanAgeSeconds\":null", capturedBody);
    }

    [Fact]
    public async Task Payload_Includes_UsbScannerLastScanAgeSeconds_After_Usb_Scan()
    {
        SetupCredentials();
        string? capturedBody = null;
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        // Do NOT call StartAsync — the background loop races with the explicit SendHeartbeatAsync
        // call and can overwrite capturedBody with a payload captured before the event fired.
        // ScanCompleted is subscribed in the constructor, so the event is captured without the loop.
        var service = BuildService();

        var usbResult = new ScanResult { Source = ScanSource.UsbScanner };
        _mockUsbScanner.Raise(s => s.ScanCompleted += null, _mockUsbScanner.Object, usbResult);

        await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("\"usbScannerLastScanAgeSeconds\":null", capturedBody);
        Assert.Contains("usbScannerLastScanAgeSeconds", capturedBody);
    }

    [Fact]
    public async Task Payload_UsbScannerLastScanAgeSeconds_Ignores_Camera_Source()
    {
        SetupSuccessfulRequest();
        string? capturedBody = null;
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        // Do NOT call StartAsync — same race concern as the USB source test above.
        var service = BuildService();

        // Raise event with Camera source — should NOT update _lastUsbScanAtUtc
        var cameraResult = new ScanResult { Source = ScanSource.Camera };
        _mockUsbScanner.Raise(s => s.ScanCompleted += null, _mockUsbScanner.Object, cameraResult);

        await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"usbScannerLastScanAgeSeconds\":null", capturedBody);
    }

    [Fact]
    public async Task Subscription_CleanedUp_On_StopAsync()
    {
        SetupCredentials();
        string? capturedBody = null;
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        var service = BuildService();
        await service.StartAsync();
        await service.StopAsync();

        // After stop, event should not update _lastUsbScanAtUtc
        var usbResult = new ScanResult { Source = ScanSource.UsbScanner };
        _mockUsbScanner.Raise(s => s.ScanCompleted += null, _mockUsbScanner.Object, usbResult);

        // SendHeartbeatAsync still works on a stopped service
        await service.SendHeartbeatAsync(CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"usbScannerLastScanAgeSeconds\":null", capturedBody);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupCredentials(string apiKey = "sk_live_test", string baseUrl = "https://192.168.10.1:8443")
    {
        _mockSecureConfig.Setup(s => s.GetApiKeyAsync()).ReturnsAsync(apiKey);
        _mockPreferences.Setup(p => p.GetServerBaseUrl()).Returns(baseUrl);
    }

    private void SetupSuccessfulRequest(string apiKey = "sk_live_test", string baseUrl = "https://192.168.10.1:8443")
    {
        SetupCredentials(apiKey, baseUrl);
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));
    }
}
