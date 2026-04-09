using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0016: Automatically syncs queued scans to the server when connectivity is restored.
/// Monitors IHealthCheckService and submits PENDING scans in FIFO batches with exponential backoff.
/// </summary>
public class BackgroundSyncService : IBackgroundSyncService, IAsyncDisposable
{
    private readonly IHealthCheckService _healthCheck;
    private readonly IOfflineQueueService _offlineQueue;
    private readonly IScanApiService _scanApi;
    private readonly IConfiguration _config;
    private readonly ILogger<BackgroundSyncService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private bool _isRunning;

    // US0016 AC3: Configurable batch size (default 50)
    private const int DefaultBatchSize = 50;

    /// <summary>
    /// US0016: Event raised when sync cycle completes (allows UI to refresh queue count).
    /// </summary>
    public event EventHandler<SyncCompletedEventArgs>? SyncCompleted;

    public BackgroundSyncService(
        IHealthCheckService healthCheck,
        IOfflineQueueService offlineQueue,
        IScanApiService scanApi,
        IConfiguration config,
        ILogger<BackgroundSyncService> logger)
    {
        _healthCheck = healthCheck;
        _offlineQueue = offlineQueue;
        _scanApi = scanApi;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// US0016 AC1: Start monitoring connectivity and trigger sync when online.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("Background sync service already running");
            return Task.CompletedTask;
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();

        // US0016 AC1: Subscribe to connectivity changes
        _healthCheck.ConnectivityChanged += OnConnectivityChanged;

        _logger.LogInformation("Background sync service started");

        // Auto-reset any scans that were permanently marked FAILED in a previous session.
        // This gives them another chance to sync instead of requiring manual intervention.
        _ = Task.Run(async () =>
        {
            try
            {
                var resetCount = await _offlineQueue.RetryFailedScansAsync();
                if (resetCount > 0)
                    _logger.LogInformation("Auto-reset {Count} permanently-failed scans back to PENDING on startup", resetCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-reset FAILED scans on startup");
            }
        });

        // US0016 AC8: If already online, trigger immediate sync
        var currentStatus = _healthCheck.IsOnline;
        _logger.LogInformation("BackgroundSync: Current health check status = {Status}",
            currentStatus == null ? "null (connecting)" : (currentStatus.Value ? "ONLINE" : "OFFLINE"));

        if (currentStatus == true)
        {
            _logger.LogInformation("BackgroundSync: Already online, triggering immediate sync");
            _ = Task.Run(async () => await TriggerSyncAsync(), _cts.Token);
        }
        else
        {
            _logger.LogInformation("BackgroundSync: Not online yet, will wait for ConnectivityChanged event");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop background sync service.
    /// </summary>
    public Task StopAsync()
    {
        if (!_isRunning)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Stopping background sync service");

        _healthCheck.ConnectivityChanged -= OnConnectivityChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isRunning = false;

        return Task.CompletedTask;
    }

    /// <summary>
    /// US0016 AC1: Handle connectivity state changes.
    /// When online → trigger sync. When offline → pause.
    /// </summary>
    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        if (isOnline)
        {
            _logger.LogInformation("Connectivity restored - triggering background sync");
            _ = Task.Run(async () => await TriggerSyncAsync());
        }
        else
        {
            _logger.LogInformation("Connectivity lost - pausing background sync");
        }
    }

    /// <summary>
    /// US0016: Manually trigger a sync cycle.
    /// Protected by SemaphoreSlim to prevent concurrent execution.
    /// </summary>
    public async Task TriggerSyncAsync()
    {
        _logger.LogInformation("TriggerSyncAsync called");

        // US0016 AC10: Use semaphore to prevent concurrent sync cycles
        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogDebug("Sync cycle already running, skipping trigger");
            return;
        }

        try
        {
            await RunSyncCycleAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background sync cycle failed");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    /// US0016 AC2/AC3: Run one sync cycle - fetch and submit batch of PENDING scans.
    /// </summary>
    private async Task RunSyncCycleAsync()
    {
        if (_healthCheck.IsOnline != true)
        {
            _logger.LogDebug("Server offline, skipping sync cycle");
            return;
        }

        var batchSize = _config.GetValue<int>("OfflineQueue:SyncBatchSize", DefaultBatchSize);

        // US0016 AC2: Get PENDING scans in FIFO order (oldest first)
        var pendingScans = await _offlineQueue.GetPendingScansAsync(batchSize);

        if (!pendingScans.Any())
        {
            _logger.LogDebug("No pending scans to sync");
            return;
        }

        _logger.LogInformation("Starting sync cycle: {Count} pending scans (batch size: {BatchSize})",
            pendingScans.Count, batchSize);

        int syncedCount = 0;
        int failedCount = 0;
        int skippedCount = 0;
        string? firstError = null;

        foreach (var scan in pendingScans)
        {
            // Check if server went offline during batch
            if (_healthCheck.IsOnline != true)
            {
                _logger.LogWarning("Server went offline mid-batch, stopping sync cycle");
                break;
            }

            // US0016 AC6: Check exponential backoff - skip if not enough time elapsed
            if (ShouldSkipDueToBackoff(scan))
            {
                skippedCount++;
                continue;
            }

            // Submit scan to server
            try
            {
                var scanType = scan.ScanType;
                var scannedAt = DateTimeOffset.Parse(scan.ScannedAt);

                // US0016: Submit via existing ScanApiService
                var result = await _scanApi.SubmitScanAsync(
                    scan.QrPayload,
                    scannedAt,
                    scanType,
                    CancellationToken.None);

                // US0016 AC4: Mark as SYNCED on success (ACCEPTED or DUPLICATE)
                if (result.Status == Models.ScanStatus.Accepted || result.Status == Models.ScanStatus.Duplicate)
                {
                    var serverScanId = result.ScanId ?? $"srv-{scan.Id}";
                    await _offlineQueue.MarkSyncedAsync(scan.Id, serverScanId);
                    syncedCount++;
                    _logger.LogInformation("Scan synced: Queue ID {QueueId} → Server ID {ServerScanId}",
                        scan.Id, serverScanId);
                }
                else
                {
                    // US0016 AC5: Mark as failed attempt (increments SyncAttempts)
                    var errorMessage = result.Message ?? $"Server returned {result.Status}";
                    await _offlineQueue.MarkFailedAsync(scan.Id, errorMessage);
                    failedCount++;

                    // Capture first error for user feedback
                    if (firstError == null)
                    {
                        firstError = errorMessage;
                    }

                    _logger.LogWarning("Scan submission failed: Queue ID {QueueId}, Status: {Status}, Error: {Error}",
                        scan.Id, result.Status, errorMessage);
                    System.Diagnostics.Debug.WriteLine($"[BackgroundSync] Scan {scan.Id} failed: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                // US0016 AC5: Network error or exception - mark as failed attempt
                _logger.LogWarning(ex, "Failed to sync scan {QueueId}", scan.Id);
                await _offlineQueue.MarkFailedAsync(scan.Id, ex.Message);
                failedCount++;

                // Capture first error for user feedback
                if (firstError == null)
                {
                    firstError = ex.Message;
                }
            }
        }

        _logger.LogInformation("Sync cycle complete: {Synced} synced, {Failed} failed, {Skipped} skipped (backoff)",
            syncedCount, failedCount, skippedCount);

        // Raise event to notify UI that sync completed
        SyncCompleted?.Invoke(this, new SyncCompletedEventArgs
        {
            SyncedCount = syncedCount,
            FailedCount = failedCount,
            SkippedCount = skippedCount,
            FirstErrorMessage = firstError
        });
    }

    /// <summary>
    /// US0016 AC6: Check if scan should be skipped due to exponential backoff.
    /// Backoff = 2^SyncAttempts * 1 second, capped at 5 minutes (300 seconds).
    /// </summary>
    private bool ShouldSkipDueToBackoff(Models.QueuedScan scan)
    {
        if (scan.SyncAttempts == 0 || string.IsNullOrEmpty(scan.LastAttemptAt))
        {
            // First attempt or no previous attempt recorded
            return false;
        }

        try
        {
            var lastAttempt = DateTimeOffset.Parse(scan.LastAttemptAt);
            var backoffSeconds = Math.Min(Math.Pow(2, scan.SyncAttempts), 300); // US0016 AC6: Cap at 5 minutes
            var nextRetryTime = lastAttempt.AddSeconds(backoffSeconds);
            var now = DateTimeOffset.UtcNow;

            if (now < nextRetryTime)
            {
                var waitSeconds = (nextRetryTime - now).TotalSeconds;
                _logger.LogDebug("Skipping scan {QueueId} due to backoff: {WaitSeconds}s remaining (attempt {Attempts})",
                    scan.Id, Math.Ceiling(waitSeconds), scan.SyncAttempts);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LastAttemptAt for scan {QueueId}, allowing retry", scan.Id);
            return false; // If parsing fails, allow retry
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _syncLock.Dispose();
    }
}
