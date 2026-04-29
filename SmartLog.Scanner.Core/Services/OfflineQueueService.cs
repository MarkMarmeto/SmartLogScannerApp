using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Data;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0014 (EP0004): SQLite-backed offline queue service.
/// Persists scans when network is unavailable for later sync.
/// </summary>
public class OfflineQueueService : IOfflineQueueService
{
    private readonly IDbContextFactory<ScannerDbContext> _dbFactory;
    private readonly ILogger<OfflineQueueService> _logger;

    public OfflineQueueService(
        IDbContextFactory<ScannerDbContext> dbFactory,
        ILogger<OfflineQueueService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// US0014 AC1: Enqueue scan to SQLite database.
    /// Extracts StudentId from QR payload for deduplication queries.
    /// </summary>
    public async Task EnqueueScanAsync(string qrPayload, DateTimeOffset scannedAt, string scanType,
        int? cameraIndex = null, string? cameraName = null)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            // Extract StudentId from payload (SMARTLOG:{studentId}:{timestamp}:{hmac})
            var studentId = ExtractStudentIdFromPayload(qrPayload);

            var queuedScan = new QueuedScan
            {
                QrPayload = qrPayload,
                StudentId = studentId,
                ScannedAt = scannedAt.UtcDateTime.ToString("o"), // ISO 8601
                ScanType = scanType,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
                SyncStatus = "PENDING",
                SyncAttempts = 0,
                CameraIndex = cameraIndex,
                CameraName = cameraName
            };

            context.QueuedScans.Add(queuedScan);
            await context.SaveChangesAsync();

            _logger.LogInformation("Scan queued offline: StudentId {StudentId}, ID: {Id}",
                studentId, queuedScan.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue scan: {QrPayload}", qrPayload);
            throw;
        }
    }

    /// <summary>
    /// Extracts the student ID from the QR payload.
    /// Format: SMARTLOG:{studentId}:{timestamp}:{hmac}
    /// </summary>
    private static string ExtractStudentIdFromPayload(string qrPayload)
    {
        var parts = qrPayload.Split(':');
        // Student: SMARTLOG:{studentId}:{timestamp}:{hmac}
        if (parts.Length >= 4 && parts[0] == "SMARTLOG")
            return parts[1];
        // Visitor: SMARTLOG-V:{passCode}:{hmac}
        if (parts.Length >= 3 && parts[0] == "SMARTLOG-V")
            return parts[1];

        return "UNKNOWN";
    }

    /// <summary>
    /// US0014 AC2: Get count of pending scans.
    /// </summary>
    public async Task<int> GetQueueCountAsync()
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            return await context.QueuedScans
                .Where(q => q.SyncStatus == "PENDING")
                .CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queue count");
            return 0;
        }
    }

    /// <summary>
    /// US0014 AC3: Get pending scans for background sync.
    /// Ordered by creation time (oldest first).
    /// </summary>
    public async Task<List<QueuedScan>> GetPendingScansAsync(int limit = 100)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            return await context.QueuedScans
                .Where(q => q.SyncStatus == "PENDING")
                .OrderBy(q => q.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending scans");
            return new List<QueuedScan>();
        }
    }

    /// <summary>
    /// US0014 AC4: Mark scan as successfully synced.
    /// </summary>
    public async Task MarkSyncedAsync(int queueId, string serverScanId)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var scan = await context.QueuedScans.FindAsync(queueId);

            if (scan != null)
            {
                scan.SyncStatus = "SYNCED";
                scan.ServerScanId = serverScanId;
                await context.SaveChangesAsync();

                _logger.LogInformation("Scan marked as synced: Queue ID {QueueId}, Server ID {ServerScanId}",
                    queueId, serverScanId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark scan as synced: {QueueId}", queueId);
            throw;
        }
    }

    /// <summary>
    /// US0014 AC5 / US0016 AC5/AC7: Mark scan as failed sync attempt.
    /// Increments retry counter, stores error message, and updates LastAttemptAt.
    /// After 10 attempts, marks scan as permanently FAILED.
    /// </summary>
    public async Task MarkFailedAsync(int queueId, string errorMessage)
    {
        const int MaxRetryAttempts = 25;

        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var scan = await context.QueuedScans.FindAsync(queueId);

            if (scan != null)
            {
                scan.SyncAttempts++;
                scan.LastSyncError = errorMessage;
                scan.LastAttemptAt = DateTimeOffset.UtcNow.ToString("o"); // US0016: Track last attempt time for backoff

                // US0016 AC7: Mark as permanently FAILED after max attempts
                if (scan.SyncAttempts >= MaxRetryAttempts)
                {
                    scan.SyncStatus = "FAILED";
                    _logger.LogError("Scan permanently FAILED after {Attempts} attempts: Queue ID {QueueId}, Error: {Error}",
                        scan.SyncAttempts, queueId, errorMessage);
                }
                else
                {
                    // US0016 AC5: Keep PENDING for retry
                    _logger.LogWarning("Scan sync attempt {Attempts}/{MaxAttempts} failed: Queue ID {QueueId}, Error: {Error}",
                        scan.SyncAttempts, MaxRetryAttempts, queueId, errorMessage);
                }

                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark scan as failed: {QueueId}", queueId);
            throw;
        }
    }

    /// <summary>
    /// US0014 AC6: Delete old synced scans for cleanup.
    /// Only deletes scans with SyncStatus = "SYNCED".
    /// </summary>
    public async Task DeleteSyncedScansAsync(int olderThanDays = 7)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-olderThanDays).ToString("o");

            var oldScans = await context.QueuedScans
                .Where(q => q.SyncStatus == "SYNCED" && q.CreatedAt.CompareTo(cutoffDate) < 0)
                .ToListAsync();

            if (oldScans.Any())
            {
                context.QueuedScans.RemoveRange(oldScans);
                await context.SaveChangesAsync();

                _logger.LogInformation("Deleted {Count} old synced scans", oldScans.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete old synced scans");
            throw;
        }
    }

    /// <summary>
    /// Checks if a pending scan already exists in the queue for the given student and scan type.
    /// Uses composite index (StudentId, ScanType, SyncStatus) for efficient queries.
    /// </summary>
    public async Task<bool> HasPendingForStudentAsync(string studentId, string scanType)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            return await context.QueuedScans
                .Where(q => q.StudentId == studentId &&
                           q.ScanType == scanType &&
                           q.SyncStatus == "PENDING")
                .AnyAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check pending queue for student {StudentId}", studentId);
            return false; // On error, allow enqueue to proceed (fail-open)
        }
    }

    /// <summary>
    /// Clears all pending scans from the queue.
    /// Used for troubleshooting or resetting failed/stuck scans.
    /// </summary>
    public async Task ClearPendingScansAsync()
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            var pendingScans = await context.QueuedScans
                .Where(q => q.SyncStatus == "PENDING")
                .ToListAsync();

            if (pendingScans.Any())
            {
                context.QueuedScans.RemoveRange(pendingScans);
                await context.SaveChangesAsync();

                _logger.LogInformation("Cleared {Count} pending scans from queue", pendingScans.Count);
            }
            else
            {
                _logger.LogInformation("No pending scans to clear");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear pending scans");
            throw;
        }
    }

    /// <summary>
    /// Gets all queued scans for display (newest first).
    /// </summary>
    public async Task<List<QueuedScan>> GetAllScansAsync(int limit = 200)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            return await context.QueuedScans
                .OrderByDescending(q => q.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all scans");
            return new List<QueuedScan>();
        }
    }

    /// <summary>
    /// Gets the count of permanently failed scans.
    /// </summary>
    public async Task<int> GetFailedCountAsync()
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            return await context.QueuedScans
                .Where(q => q.SyncStatus == "FAILED")
                .CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get failed count");
            return 0;
        }
    }

    /// <summary>
    /// Resets all FAILED scans back to PENDING for retry.
    /// </summary>
    public async Task<int> RetryFailedScansAsync()
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var failedScans = await context.QueuedScans
                .Where(q => q.SyncStatus == "FAILED")
                .ToListAsync();

            foreach (var scan in failedScans)
            {
                scan.SyncStatus = "PENDING";
                scan.SyncAttempts = 0;
                scan.LastSyncError = null;
                scan.LastAttemptAt = null;
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Reset {Count} failed scans to PENDING for retry", failedScans.Count);
            return failedScans.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry failed scans");
            throw;
        }
    }

    /// <summary>
    /// Deletes a specific queued scan by ID.
    /// </summary>
    public async Task DeleteScanAsync(int queueId)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var scan = await context.QueuedScans.FindAsync(queueId);

            if (scan != null)
            {
                context.QueuedScans.Remove(scan);
                await context.SaveChangesAsync();
                _logger.LogInformation("Deleted queued scan: ID {QueueId}", queueId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete queued scan: {QueueId}", queueId);
            throw;
        }
    }

    /// <summary>
    /// Clears all permanently failed scans from the queue.
    /// </summary>
    public async Task ClearFailedScansAsync()
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var failedScans = await context.QueuedScans
                .Where(q => q.SyncStatus == "FAILED")
                .ToListAsync();

            if (failedScans.Any())
            {
                context.QueuedScans.RemoveRange(failedScans);
                await context.SaveChangesAsync();
                _logger.LogInformation("Cleared {Count} failed scans from queue", failedScans.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear failed scans");
            throw;
        }
    }
}
