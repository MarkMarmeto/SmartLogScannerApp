namespace SmartLog.Scanner.Core.Constants;

/// <summary>
/// Configuration constants for scan deduplication service.
/// Defines tiered time windows for duplicate detection.
/// </summary>
public static class DeduplicationConfig
{
    /// <summary>
    /// SUPPRESS window: 0-2 seconds.
    /// Scans within this window are silently ignored (no UI, no sound, no API call).
    /// Handles camera jitter and USB double-tap.
    /// </summary>
    public static readonly TimeSpan SuppressWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// WARN window: 2-5 seconds.
    /// Scans within this window trigger amber "Already scanned" feedback (no API call).
    /// Student is still at the gate.
    /// </summary>
    public static readonly TimeSpan WarnWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// SERVER window: 5+ seconds.
    /// Scans after this window pass through to the server, which makes the final decision.
    /// Could be legitimate re-entry.
    /// </summary>
    public static readonly TimeSpan ServerWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cache entry TTL: 5 minutes.
    /// Deduplication cache entries older than this are evicted during periodic cleanup.
    /// </summary>
    public static readonly TimeSpan CacheEntryTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cleanup interval: 60 seconds.
    /// Periodic timer interval for evicting stale cache entries.
    /// </summary>
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Camera raw payload debounce: 500 milliseconds.
    /// Reduces the existing raw payload debounce from 2s to 500ms (performance optimization).
    /// The student-level dedup service handles the actual duplicate prevention.
    /// </summary>
    public static readonly TimeSpan CameraRawDebounce = TimeSpan.FromMilliseconds(500);
}
