using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using SmartLog.Scanner.Core.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0120: Sends scanner vitals to the admin server via POST /api/v1/devices/heartbeat.
/// Uses a Task.Delay loop (not PeriodicTimer) to support variable-interval exponential backoff.
/// </summary>
public class HeartbeatService : IHeartbeatService, IAsyncDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly IPreferencesService _preferences;
    private readonly ISecureConfigService _secureConfig;
    private readonly IOfflineQueueService _offlineQueue;
    private readonly IScanHistoryService _scanHistory;
    private readonly IQrScannerService _usbScanner;
    private readonly ILogger<HeartbeatService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _baseIntervalSeconds;
    private int _maxBackoffSeconds;
    private int _requestTimeoutSeconds;
    private int _currentIntervalSeconds;

    // EP0012/US0121: Tracks last USB scan for heartbeat health field.
    private DateTime? _lastUsbScanAtUtc;

    public HeartbeatService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        IPreferencesService preferences,
        ISecureConfigService secureConfig,
        IOfflineQueueService offlineQueue,
        IScanHistoryService scanHistory,
        IQrScannerService usbScanner,
        ILogger<HeartbeatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _preferences = preferences;
        _secureConfig = secureConfig;
        _offlineQueue = offlineQueue;
        _scanHistory = scanHistory;
        _usbScanner = usbScanner;
        _logger = logger;

        // Subscribe in the constructor so USB scan timestamps are captured even before
        // StartAsync is called, and so tests can raise the event without starting the loop.
        _usbScanner.ScanCompleted += OnUsbScan;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null)
        {
            _logger.LogWarning("HeartbeatService already started — ignoring duplicate StartAsync");
            return Task.CompletedTask;
        }

        _baseIntervalSeconds = Math.Clamp(
            _config.GetValue<int>("Heartbeat:BaseIntervalSeconds", 60), 30, 600);
        _maxBackoffSeconds = Math.Clamp(
            _config.GetValue<int>("Heartbeat:MaxBackoffSeconds", 300), 60, 3600);
        _requestTimeoutSeconds = Math.Clamp(
            _config.GetValue<int>("Heartbeat:RequestTimeoutSeconds", 10), 5, 60);
        _currentIntervalSeconds = _baseIntervalSeconds;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
        _logger.LogInformation("HeartbeatService started (base={Base}s, max={Max}s)",
            _baseIntervalSeconds, _maxBackoffSeconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;

        _usbScanner.ScanCompleted -= OnUsbScan;

        _logger.LogInformation("HeartbeatService stopping");
        await _cts.CancelAsync();

        if (_loopTask != null)
        {
            try { await _loopTask; }
            catch (OperationCanceledException) { }
        }

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var success = await SendHeartbeatAsync(ct);
                _currentIntervalSeconds = success
                    ? _baseIntervalSeconds
                    : ComputeNextInterval(_currentIntervalSeconds, false, _baseIntervalSeconds, _maxBackoffSeconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in heartbeat loop");
                _currentIntervalSeconds = ComputeNextInterval(_currentIntervalSeconds, false, _baseIntervalSeconds, _maxBackoffSeconds);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_currentIntervalSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void OnUsbScan(object? sender, ScanResult result)
    {
        if (result.Source == ScanSource.UsbScanner)
            _lastUsbScanAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Pure backoff calculation — extracted for unit testability.
    /// </summary>
    internal static int ComputeNextInterval(int currentSeconds, bool success, int baseSeconds, int maxSeconds)
    {
        if (success) return baseSeconds;
        return Math.Min(currentSeconds * 2, maxSeconds);
    }

    internal async Task<bool> SendHeartbeatAsync(CancellationToken ct)
    {
        var apiKey = await _secureConfig.GetApiKeyAsync();
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogDebug("HeartbeatService: API key not configured — skipping (setup incomplete)");
            return true; // treat as success so backoff doesn't activate on fresh installs
        }

        var baseUrl = _preferences.GetServerBaseUrl();
        if (string.IsNullOrEmpty(baseUrl))
        {
            _logger.LogDebug("HeartbeatService: server URL not configured — skipping");
            return true;
        }

        var heartbeatUrl = $"{baseUrl.TrimEnd('/')}/api/v1/devices/heartbeat";
        var acceptSelfSigned = _preferences.GetAcceptSelfSignedCerts();

        HttpClient? httpClient = null;
        HttpClientHandler? handler = null;
        bool ownsClient = false;

        try
        {
            if (acceptSelfSigned)
            {
                handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
                httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(_requestTimeoutSeconds) };
                ownsClient = true;
            }
            else
            {
                httpClient = _httpClientFactory.CreateClient("Heartbeat");
            }

            var payload = await BuildPayloadAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, heartbeatUrl);
            request.Headers.Add("X-API-Key", apiKey);
            request.Content = JsonContent.Create(payload);

            using var response = await httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Heartbeat sent successfully ({StatusCode})", (int)response.StatusCode);
                return true;
            }
            else
            {
                _logger.LogWarning("Heartbeat rejected by server ({StatusCode}) — applying backoff",
                    (int)response.StatusCode);
                return false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Loop is shutting down — propagate so RunLoopAsync breaks cleanly
            throw;
        }
        catch (OperationCanceledException)
        {
            // Request timeout (inner CTS from HttpClient.Timeout fired)
            _logger.LogWarning("Heartbeat request timed out — applying backoff");
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Heartbeat request failed (connection error) — applying backoff");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heartbeat failed with unexpected error — applying backoff");
            return false;
        }
        finally
        {
            if (ownsClient)
            {
                httpClient?.Dispose();
                handler?.Dispose();
            }
        }
    }

    private async Task<HeartbeatPayload> BuildPayloadAsync(CancellationToken ct)
    {
        string? appVersion = SafeGet(() => AppInfo.VersionString);
        string? osVersion = SafeGet(() => DeviceInfo.VersionString);

        int? batteryPercent = null;
        bool? isCharging = null;
        try
        {
            var state = Battery.Default.State;
            if (state != BatteryState.Unknown)
            {
                batteryPercent = (int)(Battery.Default.ChargeLevel * 100);
                isCharging = state == BatteryState.Charging;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Battery API unavailable on this platform — sending null");
        }

        string networkType = "OFFLINE";
        try { networkType = MapNetworkType(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Connectivity API unavailable — defaulting to OFFLINE"); }

        DateTime? lastScanAt = null;
        try
        {
            var recent = await _scanHistory.GetRecentLogsAsync(1);
            lastScanAt = recent.Count > 0 ? recent[0].Timestamp.UtcDateTime : null;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not retrieve last scan timestamp"); }

        int? queuedScansCount = null;
        try { queuedScansCount = await _offlineQueue.GetQueueCountAsync(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not retrieve offline queue count"); }

        int? usbScannerLastScanAgeSeconds = null;
        if (_lastUsbScanAtUtc.HasValue)
            usbScannerLastScanAgeSeconds = (int)(DateTime.UtcNow - _lastUsbScanAtUtc.Value).TotalSeconds;

        return new HeartbeatPayload(
            AppVersion: appVersion,
            OsVersion: osVersion,
            BatteryPercent: batteryPercent,
            IsCharging: isCharging,
            NetworkType: networkType,
            LastScanAt: lastScanAt,
            QueuedScansCount: queuedScansCount,
            UsbScannerLastScanAgeSeconds: usbScannerLastScanAgeSeconds,
            ClientTimestamp: DateTime.UtcNow);
    }

    private static string? SafeGet(Func<string> getter)
    {
        try { return getter(); }
        catch { return null; }
    }

    private static string MapNetworkType()
    {
        var access = Connectivity.NetworkAccess;
        if (access == NetworkAccess.None) return "OFFLINE";
        var profiles = Connectivity.ConnectionProfiles;
        if (profiles.Contains(ConnectionProfile.Ethernet)) return "ETHERNET";
        if (profiles.Contains(ConnectionProfile.WiFi)) return "WIFI";
        if (profiles.Contains(ConnectionProfile.Cellular)) return "CELLULAR";
        return "OFFLINE";
    }
}

internal sealed record HeartbeatPayload(
    [property: JsonPropertyName("appVersion")] string? AppVersion,
    [property: JsonPropertyName("osVersion")] string? OsVersion,
    [property: JsonPropertyName("batteryPercent")] int? BatteryPercent,
    [property: JsonPropertyName("isCharging")] bool? IsCharging,
    [property: JsonPropertyName("networkType")] string? NetworkType,
    [property: JsonPropertyName("lastScanAt")] DateTime? LastScanAt,
    [property: JsonPropertyName("queuedScansCount")] int? QueuedScansCount,
    [property: JsonPropertyName("usbScannerLastScanAgeSeconds")] int? UsbScannerLastScanAgeSeconds,
    [property: JsonPropertyName("clientTimestamp")] DateTime ClientTimestamp);
