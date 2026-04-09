namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Provides the current UTC time, optionally corrected by a server-synced clock offset.
/// All timestamp-sensitive scan operations should use this instead of DateTimeOffset.UtcNow
/// so that a misconfigured device clock doesn't corrupt attendance records.
/// </summary>
public interface ITimeService
{
    /// <summary>
    /// Current UTC time, adjusted by the server clock offset if sync succeeded.
    /// Falls back to the raw device clock if sync was never performed or failed.
    /// </summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// True if at least one successful sync has been performed this session.
    /// </summary>
    bool IsSynced { get; }

    /// <summary>
    /// The computed difference between server time and device time.
    /// Zero if not yet synced or sync failed.
    /// </summary>
    TimeSpan ClockOffset { get; }

    /// <summary>
    /// Fetches the server's current UTC time, computes the clock offset, and stores it.
    /// Safe to call multiple times — later calls update the offset.
    /// Never throws: logs and silently falls back to device clock on any failure.
    /// </summary>
    Task SyncAsync();
}
