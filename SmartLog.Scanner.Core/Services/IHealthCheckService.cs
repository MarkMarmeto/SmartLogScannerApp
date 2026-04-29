namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0015: Service for monitoring server connectivity via periodic health checks.
/// Provides real-time online/offline status to drive UI indicators and scan routing.
/// </summary>
public interface IHealthCheckService
{
    /// <summary>
    /// Current connectivity state.
    /// - true: Server is reachable (GET /api/v1/health returned 200)
    /// - false: Server is unreachable (connection failed or non-200 response)
    /// - null: Initial state before first health check completes
    /// </summary>
    bool? IsOnline { get; }

    /// <summary>
    /// Event raised when connectivity state changes.
    /// Subscribe to update UI indicators and scan routing logic.
    /// </summary>
    event EventHandler<bool>? ConnectivityChanged;

    /// <summary>
    /// Start periodic health check polling.
    /// Performs immediate first check, then polls at configured interval.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel polling</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop periodic health check polling.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Triggers an immediate health check outside the normal polling interval.
    /// Bypasses the stability window so the result is applied instantly — suitable for
    /// user-initiated refreshes where a single check should update the UI right away.
    /// </summary>
    Task CheckNowAsync();
}
