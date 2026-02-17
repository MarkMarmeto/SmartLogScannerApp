using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Constants;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Thread-safe service for student-level scan deduplication with tiered time windows.
/// Prevents duplicate scans based on studentId + scanType (independent keys for ENTRY/EXIT).
/// </summary>
public class ScanDeduplicationService : IScanDeduplicationService, IDisposable
{
    private readonly ILogger<ScanDeduplicationService> _logger;
    private readonly ConcurrentDictionary<string, ScanRecord> _cache;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public ScanDeduplicationService(ILogger<ScanDeduplicationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = new ConcurrentDictionary<string, ScanRecord>();

        // Start periodic cleanup timer (runs every 60 seconds)
        _cleanupTimer = new Timer(
            callback: _ => CleanupStaleEntries(),
            state: null,
            dueTime: DeduplicationConfig.CleanupInterval,
            period: DeduplicationConfig.CleanupInterval);

        _logger.LogInformation("ScanDeduplicationService initialized with tiered windows: " +
                               "SUPPRESS={SuppressMs}ms, WARN={WarnSec}s, TTL={TtlMin}min",
                               DeduplicationConfig.SuppressWindow.TotalMilliseconds,
                               DeduplicationConfig.WarnWindow.TotalSeconds,
                               DeduplicationConfig.CacheEntryTtl.TotalMinutes);
    }

    /// <summary>
    /// Checks if a scan should be processed and records it if allowed.
    /// Thread-safe using ConcurrentDictionary.AddOrUpdate for atomicity.
    /// </summary>
    public DeduplicationResult CheckAndRecord(string studentId, string scanType, string? studentName = null)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("Student ID cannot be null or empty", nameof(studentId));
        if (string.IsNullOrWhiteSpace(scanType))
            throw new ArgumentException("Scan type cannot be null or empty", nameof(scanType));

        var key = BuildCacheKey(studentId, scanType);
        var now = DateTimeOffset.UtcNow;

        // AddOrUpdate for atomic check-and-update
        var record = _cache.AddOrUpdate(
            key: key,
            addValueFactory: _ =>
            {
                // First scan for this student+scanType - allow it
                _logger.LogDebug("First scan for {Key}, allowing", key);
                return new ScanRecord(now, studentName);
            },
            updateValueFactory: (_, existingRecord) =>
            {
                // Existing scan found - check time windows
                var timeSinceLastScan = now - existingRecord.LastAcceptedAt;

                if (timeSinceLastScan < DeduplicationConfig.SuppressWindow)
                {
                    // SUPPRESS window: 0-2 seconds - return existing record unchanged
                    _logger.LogDebug("Scan within SUPPRESS window ({Ms}ms) for {Key}, suppressing silently",
                                     timeSinceLastScan.TotalMilliseconds, key);
                    return existingRecord;
                }
                else if (timeSinceLastScan < DeduplicationConfig.WarnWindow)
                {
                    // WARN window: 2-30 seconds - return existing record unchanged
                    _logger.LogDebug("Scan within WARN window ({Sec}s) for {Key}, rejecting with feedback",
                                     timeSinceLastScan.TotalSeconds, key);
                    return existingRecord;
                }
                else
                {
                    // SERVER window: 30+ seconds - update record and allow
                    _logger.LogDebug("Scan after SERVER window ({Sec}s) for {Key}, allowing",
                                     timeSinceLastScan.TotalSeconds, key);
                    return new ScanRecord(now, studentName ?? existingRecord.StudentName);
                }
            });

        // Determine action based on whether the record was updated
        var timeSinceLastScan = now - record.LastAcceptedAt;

        // First scan detection: if timeSinceLastScan is essentially zero (< 1ms),
        // this is a newly created record, so allow it to proceed
        if (timeSinceLastScan < TimeSpan.FromMilliseconds(1))
        {
            // First scan - allow it to proceed to server
            _logger.LogDebug("First scan detected (0ms delta), proceeding to submission");
            return new DeduplicationResult(
                Action: DeduplicationAction.Proceed,
                TimeSinceLastScan: timeSinceLastScan,
                Message: null);
        }
        else if (timeSinceLastScan < DeduplicationConfig.SuppressWindow)
        {
            // Within SUPPRESS window: silent suppression
            return new DeduplicationResult(
                Action: DeduplicationAction.SuppressSilent,
                TimeSinceLastScan: timeSinceLastScan,
                Message: null);
        }
        else if (timeSinceLastScan < DeduplicationConfig.WarnWindow)
        {
            // Within WARN window: reject with feedback
            var displayName = record.StudentName ?? studentId;
            var message = $"{displayName} already scanned. Please proceed.";

            return new DeduplicationResult(
                Action: DeduplicationAction.RejectWithFeedback,
                TimeSinceLastScan: timeSinceLastScan,
                Message: message);
        }
        else
        {
            // Beyond warn window: allow scan to proceed
            return new DeduplicationResult(
                Action: DeduplicationAction.Proceed,
                TimeSinceLastScan: timeSinceLastScan,
                Message: null);
        }
    }

    /// <summary>
    /// Clears all deduplication records.
    /// </summary>
    public void Reset()
    {
        _cache.Clear();
        _logger.LogInformation("Deduplication cache cleared");
    }

    /// <summary>
    /// Builds the cache key from studentId and scanType.
    /// ENTRY and EXIT are independent keys (toggling scan type never interferes).
    /// </summary>
    private static string BuildCacheKey(string studentId, string scanType)
    {
        return $"{studentId}:{scanType}";
    }

    /// <summary>
    /// Periodic cleanup: evicts entries older than CacheEntryTtl (5 minutes).
    /// </summary>
    private void CleanupStaleEntries()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var cutoffTime = now - DeduplicationConfig.CacheEntryTtl;
            var keysToRemove = new List<string>();

            foreach (var kvp in _cache)
            {
                if (kvp.Value.LastAcceptedAt < cutoffTime)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_cache.TryRemove(key, out _))
                {
                    _logger.LogDebug("Evicted stale cache entry: {Key}", key);
                }
            }

            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation("Cleanup removed {Count} stale entries, {Remaining} remaining",
                                       keysToRemove.Count, _cache.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during deduplication cache cleanup");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            _cache.Clear();
            _disposed = true;
            _logger.LogInformation("ScanDeduplicationService disposed");
        }
    }

    /// <summary>
    /// Record of a student scan for deduplication tracking.
    /// </summary>
    private record ScanRecord(DateTimeOffset LastAcceptedAt, string? StudentName);
}
