namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0120: Periodic heartbeat sender. POSTs scanner vitals to the admin server every 60 s.
/// Distinct from IHealthCheckService — health check pulls (GET) for local online indicator;
/// heartbeat pushes (POST) for admin-side health monitoring.
/// </summary>
public interface IHeartbeatService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
