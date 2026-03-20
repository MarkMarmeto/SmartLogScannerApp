using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Service for tracking and retrieving scan history/logs.
/// Provides visibility into all scan attempts for diagnostics and troubleshooting.
/// </summary>
public interface IScanHistoryService
{
    /// <summary>
    /// Record a scan attempt with all details
    /// </summary>
    Task LogScanAsync(ScanLogEntry entry);

    /// <summary>
    /// Get recent scan logs (most recent first)
    /// </summary>
    Task<List<ScanLogEntry>> GetRecentLogsAsync(int count = 100);

    /// <summary>
    /// Get all logs for a specific date
    /// </summary>
    Task<List<ScanLogEntry>> GetLogsByDateAsync(DateTimeOffset date);

    /// <summary>
    /// Get logs filtered by status
    /// </summary>
    Task<List<ScanLogEntry>> GetLogsByStatusAsync(ScanStatus status);

    /// <summary>
    /// Search logs by student ID or name
    /// </summary>
    Task<List<ScanLogEntry>> SearchLogsAsync(string searchTerm);

    /// <summary>
    /// Get summary statistics for today
    /// </summary>
    Task<ScanStatistics> GetTodayStatisticsAsync();

    /// <summary>
    /// Clear old logs (older than specified days)
    /// </summary>
    Task ClearOldLogsAsync(int olderThanDays = 30);

    /// <summary>
    /// Export logs to CSV format
    /// </summary>
    Task<string> ExportLogsAsync(DateTimeOffset? startDate = null, DateTimeOffset? endDate = null);

    /// <summary>
    /// Get total log count
    /// </summary>
    Task<int> GetLogCountAsync();
}

/// <summary>
/// Summary statistics for scans
/// </summary>
public class ScanStatistics
{
    public int TotalScans { get; set; }
    public int Accepted { get; set; }
    public int Duplicates { get; set; }
    public int Rejected { get; set; }
    public int Errors { get; set; }
    public int QueuedOffline { get; set; }
    public double AverageProcessingTimeMs { get; set; }
    public int UniqueStudents { get; set; }
}
