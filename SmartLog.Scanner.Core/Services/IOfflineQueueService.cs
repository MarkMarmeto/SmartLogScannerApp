using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0014 (EP0004): Service for managing offline scan queue with SQLite persistence.
/// </summary>
public interface IOfflineQueueService
{
    /// <summary>
    /// Enqueues a scan for later submission when network is restored.
    /// </summary>
    /// <param name="qrPayload">Complete QR code payload</param>
    /// <param name="scannedAt">Timestamp when the QR was scanned</param>
    /// <param name="scanType">Scan direction: "ENTRY" or "EXIT"</param>
    Task EnqueueScanAsync(string qrPayload, DateTimeOffset scannedAt, string scanType);

    /// <summary>
    /// Gets the count of pending queued scans (SyncStatus = "PENDING").
    /// </summary>
    Task<int> GetQueueCountAsync();

    /// <summary>
    /// Gets all pending scans ordered by creation time (oldest first).
    /// Used by background sync service.
    /// </summary>
    Task<List<QueuedScan>> GetPendingScansAsync(int limit = 100);

    /// <summary>
    /// Marks a scan as successfully synced with the server.
    /// </summary>
    /// <param name="queueId">Queue record ID</param>
    /// <param name="serverScanId">Server-assigned scan ID</param>
    Task MarkSyncedAsync(int queueId, string serverScanId);

    /// <summary>
    /// Marks a scan as failed sync attempt.
    /// </summary>
    /// <param name="queueId">Queue record ID</param>
    /// <param name="errorMessage">Error description</param>
    Task MarkFailedAsync(int queueId, string errorMessage);

    /// <summary>
    /// Deletes synced scans older than the specified number of days.
    /// Used for cleanup of successfully synced records.
    /// </summary>
    /// <param name="olderThanDays">Age threshold in days</param>
    Task DeleteSyncedScansAsync(int olderThanDays = 7);

    /// <summary>
    /// Checks if a pending scan already exists in the queue for the given student and scan type.
    /// Used for offline queue deduplication to prevent duplicate queued entries.
    /// </summary>
    /// <param name="studentId">The student ID to check</param>
    /// <param name="scanType">The scan type ("ENTRY" or "EXIT")</param>
    /// <returns>True if a PENDING scan exists for this student+scanType, false otherwise</returns>
    Task<bool> HasPendingForStudentAsync(string studentId, string scanType);
}
