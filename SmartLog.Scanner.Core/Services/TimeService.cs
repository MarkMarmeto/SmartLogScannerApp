using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Syncs the device clock against the server once at startup and applies the offset
/// to every subsequent UtcNow call. Falls back silently to the raw device clock
/// if the server is unreachable or the URL is not yet configured.
/// </summary>
public class TimeService : ITimeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPreferencesService _preferences;
    private readonly ILogger<TimeService> _logger;

    private TimeSpan _clockOffset = TimeSpan.Zero;
    private bool _isSynced;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + _clockOffset;
    public bool IsSynced => _isSynced;
    public TimeSpan ClockOffset => _clockOffset;

    public TimeService(
        IHttpClientFactory httpClientFactory,
        IPreferencesService preferences,
        ILogger<TimeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _preferences = preferences;
        _logger = logger;
    }

    public async Task SyncAsync()
    {
        var baseUrl = _preferences.GetServerBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("TimeService: server URL not configured — skipping clock sync");
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("HealthCheck");
            var url = $"{baseUrl.TrimEnd('/')}/api/v1/health/time";

            // Bracket the request so we can estimate network latency and pick the midpoint
            var t0 = DateTimeOffset.UtcNow;
            var response = await client.GetAsync(url);
            var t1 = DateTimeOffset.UtcNow;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TimeService: server returned {StatusCode} — keeping existing offset", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("utc", out var utcElement))
            {
                _logger.LogWarning("TimeService: invalid response body — keeping existing offset");
                return;
            }
            var utcString = utcElement.GetString();
            if (utcString == null)
            {
                _logger.LogWarning("TimeService: invalid response body — keeping existing offset");
                return;
            }

            var serverTime = DateTimeOffset.Parse(utcString);

            // Approximate the server time at the midpoint of the round-trip
            var deviceMidpoint = t0 + (t1 - t0) / 2;
            _clockOffset = serverTime - deviceMidpoint;
            _isSynced = true;

            var offsetSeconds = _clockOffset.TotalSeconds;
            _logger.LogInformation(
                "TimeService: clock sync OK. Offset={Offset:+0.###;-0.###}s (round-trip {Rtt}ms)",
                offsetSeconds, (t1 - t0).TotalMilliseconds);

            if (Math.Abs(offsetSeconds) > 30)
            {
                _logger.LogWarning(
                    "TimeService: device clock is off by {Seconds:F1}s — timestamps will be corrected automatically",
                    offsetSeconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TimeService: sync failed — using device clock as fallback");
        }
    }

}
