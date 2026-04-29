using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Service for student-level scan deduplication with tiered time windows.
/// Prevents duplicate scans based on studentId + scanType (not raw payload).
/// </summary>
public interface IScanDeduplicationService
{
    /// <summary>
    /// Checks if a scan should be processed and records it if allowed.
    /// Uses tiered time windows: SUPPRESS (0-2s), WARN (2-30s), SERVER (30s+).
    /// </summary>
    /// <param name="studentId">The student ID from the validated QR code.</param>
    /// <param name="scanType">The scan type ("ENTRY" or "EXIT").</param>
    /// <param name="studentName">Optional student name for cached display in feedback messages.</param>
    /// <returns>
    /// DeduplicationResult indicating the action to take:
    /// - Proceed: Allow scan to continue to server submission
    /// - SuppressSilent: Ignore silently (within 2s suppress window)
    /// - RejectWithFeedback: Show amber "Already scanned" message (within 30s warn window)
    /// </returns>
    DeduplicationResult CheckAndRecord(string studentId, string scanType, string? studentName = null);

    /// <summary>
    /// Removes the deduplication record for a specific key+scanType, allowing the next scan
    /// to proceed to the server immediately. Call this when the server rejects a scan so that
    /// a rejected QR (e.g. deactivated pass, inactive student) does not block subsequent
    /// re-scans with a false "Duplicate" warning.
    /// </summary>
    void Remove(string key, string scanType);

    /// <summary>
    /// Clears all deduplication records.
    /// Used for testing or manual cache reset.
    /// </summary>
    void Reset();
}
