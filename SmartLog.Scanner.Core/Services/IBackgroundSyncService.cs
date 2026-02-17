namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0016: Service for automatically syncing queued scans to the server
/// when connectivity is restored.
/// </summary>
public interface IBackgroundSyncService
{
    /// <summary>
    /// Start the background sync service.
    /// Monitors health check service and syncs PENDING scans when online.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the background sync service.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Manually trigger a sync cycle.
    /// Used for testing or user-initiated sync (out of scope for US0016, but reserved for future use).
    /// </summary>
    Task TriggerSyncAsync();
}
