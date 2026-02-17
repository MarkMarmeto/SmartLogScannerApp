namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// US0010: Scan submission result status.
/// </summary>
public enum ScanStatus
{
    /// <summary>
    /// Student scan recorded successfully.
    /// </summary>
    Accepted,

    /// <summary>
    /// Student already scanned; informational.
    /// </summary>
    Duplicate,

    /// <summary>
    /// QR code invalid, student inactive, or QR invalidated.
    /// </summary>
    Rejected,

    /// <summary>
    /// API key invalid; device needs re-registration.
    /// </summary>
    Error,

    /// <summary>
    /// Too many requests; retry after specified period.
    /// </summary>
    RateLimited,

    /// <summary>
    /// Network error; scan saved to offline queue.
    /// </summary>
    Queued,

    /// <summary>
    /// Scan debounced locally (duplicate student+scanType within warn window).
    /// </summary>
    DebouncedLocally
}
