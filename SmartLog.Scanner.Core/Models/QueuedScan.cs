using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// US0014: SQLite entity for offline scan queue.
/// Stores scans when network is unavailable for later sync.
/// </summary>
public class QueuedScan
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Original QR code payload (SMARTLOG:{studentId}:{timestamp}:{hmac})
    /// </summary>
    [Required]
    public string QrPayload { get; set; } = string.Empty;

    /// <summary>
    /// Student ID extracted from QR payload (for deduplication queries)
    /// </summary>
    [Required]
    public string StudentId { get; set; } = string.Empty;

    /// <summary>
    /// When the QR code was scanned (ISO 8601 UTC)
    /// </summary>
    [Required]
    public string ScannedAt { get; set; } = string.Empty;

    /// <summary>
    /// Scan type: "ENTRY" or "EXIT"
    /// </summary>
    [Required]
    public string ScanType { get; set; } = string.Empty;

    /// <summary>
    /// When this record was created in the queue (ISO 8601 UTC)
    /// </summary>
    [Required]
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    /// Sync status: PENDING, SYNCED, FAILED
    /// </summary>
    [Required]
    public string SyncStatus { get; set; } = "PENDING";

    /// <summary>
    /// Number of times sync has been attempted
    /// </summary>
    public int SyncAttempts { get; set; } = 0;

    /// <summary>
    /// Last error message from sync attempt (if any)
    /// </summary>
    public string? LastSyncError { get; set; }

    /// <summary>
    /// Server-assigned scan ID after successful sync
    /// </summary>
    public string? ServerScanId { get; set; }

    /// <summary>
    /// US0016: Timestamp of last sync attempt (for exponential backoff calculation)
    /// </summary>
    public string? LastAttemptAt { get; set; }
}
