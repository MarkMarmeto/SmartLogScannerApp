using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Data;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Implementation of scan history tracking service using SQLite database.
/// Stores all scan attempts for diagnostics, troubleshooting, and auditing.
/// </summary>
public class ScanHistoryService : IScanHistoryService
{
    private readonly IDbContextFactory<ScannerDbContext> _contextFactory;
    private readonly ILogger<ScanHistoryService> _logger;

    public ScanHistoryService(
        IDbContextFactory<ScannerDbContext> contextFactory,
        ILogger<ScanHistoryService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task LogScanAsync(ScanLogEntry entry)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            context.ScanLogs.Add(entry);
            await context.SaveChangesAsync();

            _logger.LogDebug("Scan log recorded: {StudentId} - {Status}",
                entry.StudentId ?? "Unknown", entry.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log scan entry");
            // Don't throw - logging failure shouldn't break scanning
        }
    }

    public async Task<List<ScanLogEntry>> GetRecentLogsAsync(int count = 100)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            // SQLite doesn't support DateTimeOffset in ORDER BY, so sort client-side
            var logs = await context.ScanLogs.ToListAsync();
            return logs
                .OrderByDescending(log => log.Timestamp)
                .Take(count)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve recent logs");
            return new List<ScanLogEntry>();
        }
    }

    public async Task<List<ScanLogEntry>> GetLogsByDateAsync(DateTimeOffset date)
    {
        try
        {
            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1);

            await using var context = await _contextFactory.CreateDbContextAsync();
            var logs = await context.ScanLogs.ToListAsync();
            return logs
                .Where(log => log.Timestamp >= startOfDay && log.Timestamp < endOfDay)
                .OrderByDescending(log => log.Timestamp)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve logs by date: {Date}", date);
            return new List<ScanLogEntry>();
        }
    }

    public async Task<List<ScanLogEntry>> GetLogsByStatusAsync(ScanStatus status)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var logs = await context.ScanLogs.ToListAsync();
            return logs
                .Where(log => log.Status == status)
                .OrderByDescending(log => log.Timestamp)
                .Take(100)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve logs by status: {Status}", status);
            return new List<ScanLogEntry>();
        }
    }

    public async Task<List<ScanLogEntry>> SearchLogsAsync(string searchTerm)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return await GetRecentLogsAsync();
            }

            var term = searchTerm.ToLowerInvariant();

            await using var context = await _contextFactory.CreateDbContextAsync();
            var logs = await context.ScanLogs.ToListAsync();
            return logs
                .Where(log =>
                    (log.StudentId != null && log.StudentId.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (log.StudentName != null && log.StudentName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (log.ScanId != null && log.ScanId.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(log => log.Timestamp)
                .Take(100)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search logs: {SearchTerm}", searchTerm);
            return new List<ScanLogEntry>();
        }
    }

    public async Task<ScanStatistics> GetTodayStatisticsAsync()
    {
        try
        {
            var today = DateTimeOffset.Now.Date;
            var tomorrow = today.AddDays(1);

            await using var context = await _contextFactory.CreateDbContextAsync();
            var allLogs = await context.ScanLogs.ToListAsync();
            var todayLogs = allLogs
                .Where(log => log.Timestamp >= today && log.Timestamp < tomorrow)
                .ToList();

            return new ScanStatistics
            {
                TotalScans = todayLogs.Count,
                Accepted = todayLogs.Count(l => l.Status == ScanStatus.Accepted),
                Duplicates = todayLogs.Count(l => l.Status == ScanStatus.Duplicate),
                Rejected = todayLogs.Count(l => l.Status == ScanStatus.Rejected),
                Errors = todayLogs.Count(l => l.Status == ScanStatus.Error),
                QueuedOffline = todayLogs.Count(l => l.Status == ScanStatus.Queued),
                AverageProcessingTimeMs = todayLogs.Any() ? todayLogs.Average(l => l.ProcessingTimeMs) : 0,
                UniqueStudents = todayLogs.Where(l => !string.IsNullOrEmpty(l.StudentId))
                    .Select(l => l.StudentId)
                    .Distinct()
                    .Count()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate today's statistics");
            return new ScanStatistics();
        }
    }

    public async Task ClearOldLogsAsync(int olderThanDays = 30)
    {
        try
        {
            var cutoffDate = DateTimeOffset.Now.AddDays(-olderThanDays);

            await using var context = await _contextFactory.CreateDbContextAsync();
            var allLogs = await context.ScanLogs.ToListAsync();
            var oldLogs = allLogs
                .Where(log => log.Timestamp < cutoffDate)
                .ToList();

            if (oldLogs.Any())
            {
                context.ScanLogs.RemoveRange(oldLogs);
                await context.SaveChangesAsync();

                _logger.LogInformation("Cleared {Count} old log entries (older than {Days} days)",
                    oldLogs.Count, olderThanDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear old logs");
        }
    }

    public async Task<string> ExportLogsAsync(DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
    {
        try
        {
            var start = startDate ?? DateTimeOffset.Now.AddDays(-7);
            var end = endDate ?? DateTimeOffset.Now;

            await using var context = await _contextFactory.CreateDbContextAsync();
            var allLogs = await context.ScanLogs.ToListAsync();
            var logs = allLogs
                .Where(log => log.Timestamp >= start && log.Timestamp <= end)
                .OrderBy(log => log.Timestamp)
                .ToList();

            // Build CSV
            var csv = new StringBuilder();
            csv.AppendLine("Timestamp,StudentID,StudentName,ScanType,Status,Message,ScanMethod,NetworkAvailable,ProcessingTimeMs");

            foreach (var log in logs)
            {
                csv.AppendLine($"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                              $"\"{log.StudentId ?? ""}\"," +
                              $"\"{log.StudentName ?? ""}\"," +
                              $"\"{log.ScanType}\"," +
                              $"\"{log.Status}\"," +
                              $"\"{log.Message?.Replace("\"", "\"\"")}\"," +
                              $"\"{log.ScanMethod}\"," +
                              $"{log.NetworkAvailable}," +
                              $"{log.ProcessingTimeMs}");
            }

            _logger.LogInformation("Exported {Count} log entries to CSV", logs.Count);
            return csv.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export logs");
            return string.Empty;
        }
    }

    public async Task<int> GetLogCountAsync()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ScanLogs.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get log count");
            return 0;
        }
    }
}
