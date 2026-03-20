using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0015: Monitors server connectivity via periodic GET /api/v1/health polls.
/// </summary>
public class HealthCheckService : IHealthCheckService, IAsyncDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly IPreferencesService _preferences;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;

    private bool? _isOnline = true; // OPTIMISTIC: Assume online until proven offline (prevents queueing on startup)
    private string? _cachedServerUrl = null; // Cache to avoid disk I/O on every poll
    private int _consecutiveSuccesses = 0; // Stability window: consecutive successful checks
    private int _consecutiveFailures = 0; // Stability window: consecutive failed checks
    private const int StabilityThreshold = 2; // Require 2 consecutive results before changing status

    /// <summary>
    /// US0015 AC3/AC4: Current connectivity state.
    /// </summary>
    public bool? IsOnline
    {
        get => _isOnline;
        private set
        {
            if (_isOnline != value)
            {
                var previousState = _isOnline;
                _isOnline = value;

                // US0015 AC5/AC6: Log connectivity state changes
                if (value == true)
                {
                    _logger.LogInformation("Server connectivity restored - now ONLINE");
                }
                else if (value == false)
                {
                    _logger.LogWarning("Server connectivity lost - now OFFLINE");
                }

                // Raise event when transitioning to a known state (including initial transition from null)
                if (value != null)
                {
                    ConnectivityChanged?.Invoke(this, value.Value);
                }
            }
        }
    }

    /// <summary>
    /// US0015: Event raised when connectivity changes (online ↔ offline).
    /// </summary>
    public event EventHandler<bool>? ConnectivityChanged;

    public HealthCheckService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        IPreferencesService preferences,
        ILogger<HealthCheckService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _preferences = preferences;
        _logger = logger;
    }

    /// <summary>
    /// US0015 AC1/AC2: Start periodic health check polling.
    /// Performs immediate first check, then polls at configured interval (default 15s).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_timer != null)
        {
            _logger.LogWarning("Health check service already started");
            return Task.CompletedTask;
        }

        // US0015 AC2: Read poll interval from configuration (default: 15 seconds)
        var intervalSeconds = _config.GetValue<int>("OfflineQueue:HealthCheckIntervalSeconds", 15);
        _logger.LogInformation("Starting health check service with {Interval}s interval", intervalSeconds);

        _pollingCts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        // Start background polling task
        _pollingTask = Task.Run(async () =>
        {
            // US0015 AC7: Immediate first check before first interval
            await CheckHealthAsync();

            // Then periodic checks
            while (await _timer.WaitForNextTickAsync(_pollingCts.Token))
            {
                await CheckHealthAsync();
            }
        }, _pollingCts.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop periodic health check polling.
    /// </summary>
    public async Task StopAsync()
    {
        if (_timer == null)
        {
            return;
        }

        _logger.LogInformation("Stopping health check service");

        _pollingCts?.Cancel();
        _timer?.Dispose();
        _timer = null;

        if (_pollingTask != null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
        }

        _pollingCts?.Dispose();
        _pollingCts = null;
        _pollingTask = null;
    }

    /// <summary>
    /// US0015 AC2/AC3/AC4: Perform single health check poll.
    /// Sends GET /api/v1/health (unauthenticated) and updates IsOnline.
    /// </summary>
    private async Task CheckHealthAsync()
    {
        // US0015: Serialize concurrent polls (no overlapping requests)
        if (!await _pollLock.WaitAsync(0))
        {
            _logger.LogDebug("Previous health check still running, skipping this poll");
            return;
        }

        try
        {
            // US0015 AC8: Use dedicated HealthCheck client (no Polly retry/circuit breaker)
            var httpClient = _httpClientFactory.CreateClient("HealthCheck");

            // Cache server URL to avoid repeated lookups on every poll
            if (_cachedServerUrl == null)
            {
                _cachedServerUrl = _preferences.GetServerBaseUrl();
            }

            if (string.IsNullOrEmpty(_cachedServerUrl))
            {
                _logger.LogDebug("Server URL not configured yet (setup incomplete)");
                IsOnline = false;
                return;
            }

            var healthUrl = $"{_cachedServerUrl.TrimEnd('/')}/api/v1/health";

            // US0015 AC2: GET /api/v1/health (no X-API-Key header)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 10s timeout
            var response = await httpClient.GetAsync(healthUrl, cts.Token);

            // US0015 AC3: 200 = online (with stability window)
            if (response.IsSuccessStatusCode)
            {
                _consecutiveSuccesses++;
                _consecutiveFailures = 0;

                // Only mark online after StabilityThreshold consecutive successes
                if (_consecutiveSuccesses >= StabilityThreshold || _isOnline == null)
                {
                    IsOnline = true;
                }
                else
                {
                    _logger.LogDebug("Health check success ({Consecutive}/{Threshold}), waiting for stability",
                        _consecutiveSuccesses, StabilityThreshold);
                }
            }
            else
            {
                // Non-200 status = offline (with stability window)
                _consecutiveFailures++;
                _consecutiveSuccesses = 0;

                _logger.LogWarning("Health check returned {StatusCode} ({Consecutive}/{Threshold})",
                    response.StatusCode, _consecutiveFailures, StabilityThreshold);

                // Only mark offline after StabilityThreshold consecutive failures
                if (_consecutiveFailures >= StabilityThreshold)
                {
                    IsOnline = false;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            // US0015 AC4: Connection refused, DNS failure, etc.
            _consecutiveFailures++;
            _consecutiveSuccesses = 0;

            _logger.LogDebug(ex, "Health check failed - connection error ({Consecutive}/{Threshold})",
                _consecutiveFailures, StabilityThreshold);

            if (_consecutiveFailures >= StabilityThreshold)
            {
                IsOnline = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout
            _consecutiveFailures++;
            _consecutiveSuccesses = 0;

            _logger.LogDebug("Health check timed out ({Consecutive}/{Threshold})",
                _consecutiveFailures, StabilityThreshold);

            if (_consecutiveFailures >= StabilityThreshold)
            {
                IsOnline = false;
            }
        }
        catch (Exception ex)
        {
            // Unexpected error
            _consecutiveFailures++;
            _consecutiveSuccesses = 0;

            _logger.LogError(ex, "Health check failed with unexpected error ({Consecutive}/{Threshold})",
                _consecutiveFailures, StabilityThreshold);

            if (_consecutiveFailures >= StabilityThreshold)
            {
                IsOnline = false;
            }
        }
        finally
        {
            _pollLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _pollLock.Dispose();
    }
}
