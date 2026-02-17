namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// Defines the action to take after deduplication check.
/// </summary>
public enum DeduplicationAction
{
    /// <summary>
    /// Allow the scan to proceed to validation and submission.
    /// </summary>
    Proceed,

    /// <summary>
    /// Silently suppress the scan with no UI feedback (within suppress window).
    /// </summary>
    SuppressSilent,

    /// <summary>
    /// Reject the scan with user feedback (within warn window).
    /// </summary>
    RejectWithFeedback
}

/// <summary>
/// Result of a deduplication check for a student scan.
/// </summary>
/// <param name="Action">The action to take for this scan.</param>
/// <param name="TimeSinceLastScan">Time elapsed since the last scan of this student+scanType.</param>
/// <param name="Message">Optional message to display to the user (for RejectWithFeedback).</param>
public record DeduplicationResult(
    DeduplicationAction Action,
    TimeSpan TimeSinceLastScan,
    string? Message);
