using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Core.ViewModels;

/// <summary>
/// ViewModel for Scan Logs Viewer page.
/// Displays scan history, statistics, and provides search/filter capabilities.
/// </summary>
public partial class ScanLogsViewModel : ObservableObject
{
    private readonly IScanHistoryService _scanHistory;
    private readonly ILogger<ScanLogsViewModel> _logger;

    [ObservableProperty] private ObservableCollection<ScanLogEntry> _logs = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // Statistics
    [ObservableProperty] private int _totalScans;
    [ObservableProperty] private int _acceptedCount;
    [ObservableProperty] private int _duplicateCount;
    [ObservableProperty] private int _rejectedCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _queuedCount;
    [ObservableProperty] private int _uniqueStudents;
    [ObservableProperty] private double _averageProcessingTime;

    // Filters
    [ObservableProperty] private bool _showAll = true;
    [ObservableProperty] private bool _showAccepted;
    [ObservableProperty] private bool _showDuplicates;
    [ObservableProperty] private bool _showRejected;
    [ObservableProperty] private bool _showErrors;

    // Selected log for details
    [ObservableProperty] private ScanLogEntry? _selectedLog;

    public ScanLogsViewModel(
        IScanHistoryService scanHistory,
        ILogger<ScanLogsViewModel> logger)
    {
        _scanHistory = scanHistory;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the page and load recent logs
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadRecentLogsAsync();
        await LoadStatisticsAsync();
    }

    /// <summary>
    /// Load recent scan logs (last 100)
    /// </summary>
    [RelayCommand]
    private async Task LoadRecentLogsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var logs = await _scanHistory.GetRecentLogsAsync(100);
            Logs = new ObservableCollection<ScanLogEntry>(logs);

            _logger.LogInformation("Loaded {Count} recent scan logs", logs.Count);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load logs: {ex.Message}";
            _logger.LogError(ex, "Failed to load recent logs");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load today's statistics
    /// </summary>
    [RelayCommand]
    private async Task LoadStatisticsAsync()
    {
        try
        {
            var stats = await _scanHistory.GetTodayStatisticsAsync();

            TotalScans = stats.TotalScans;
            AcceptedCount = stats.Accepted;
            DuplicateCount = stats.Duplicates;
            RejectedCount = stats.Rejected;
            ErrorCount = stats.Errors;
            QueuedCount = stats.QueuedOffline;
            UniqueStudents = stats.UniqueStudents;
            AverageProcessingTime = Math.Round(stats.AverageProcessingTimeMs, 2);

            _logger.LogInformation("Loaded today's statistics: {Total} total scans", stats.TotalScans);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load statistics");
        }
    }

    /// <summary>
    /// Search logs by student ID or name
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            await LoadRecentLogsAsync();
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var logs = await _scanHistory.SearchLogsAsync(SearchText);
            Logs = new ObservableCollection<ScanLogEntry>(logs);

            _logger.LogInformation("Search returned {Count} results for '{Term}'", logs.Count, SearchText);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
            _logger.LogError(ex, "Search failed for term: {Term}", SearchText);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Filter logs by status
    /// </summary>
    [RelayCommand]
    private async Task FilterByStatusAsync(string status)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            List<ScanLogEntry> logs;

            if (status == "All")
            {
                logs = await _scanHistory.GetRecentLogsAsync(100);
            }
            else if (Enum.TryParse<ScanStatus>(status, out var scanStatus))
            {
                logs = await _scanHistory.GetLogsByStatusAsync(scanStatus);
            }
            else
            {
                logs = await _scanHistory.GetRecentLogsAsync(100);
            }

            Logs = new ObservableCollection<ScanLogEntry>(logs);

            _logger.LogInformation("Filtered logs by status: {Status}, found {Count}", status, logs.Count);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Filter failed: {ex.Message}";
            _logger.LogError(ex, "Filter failed for status: {Status}", status);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load logs for a specific date
    /// </summary>
    [RelayCommand]
    private async Task LoadDateLogsAsync(DateTimeOffset date)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var logs = await _scanHistory.GetLogsByDateAsync(date);
            Logs = new ObservableCollection<ScanLogEntry>(logs);

            _logger.LogInformation("Loaded {Count} logs for date: {Date}", logs.Count, date.ToString("yyyy-MM-dd"));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load logs for date: {ex.Message}";
            _logger.LogError(ex, "Failed to load logs for date: {Date}", date);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Export logs to CSV
    /// </summary>
    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var csv = await _scanHistory.ExportLogsAsync();

            if (string.IsNullOrEmpty(csv))
            {
                ErrorMessage = "No logs to export";
                return;
            }

            // Save to file
            var fileName = $"scan-logs-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}.csv";
            var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);

            await File.WriteAllTextAsync(filePath, csv);

            _logger.LogInformation("Exported logs to: {FilePath}", filePath);

            // TODO: Show success message or share file
            ErrorMessage = $"Exported to: {filePath}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
            _logger.LogError(ex, "Failed to export logs");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refresh all data
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadRecentLogsAsync();
        await LoadStatisticsAsync();
    }

    /// <summary>
    /// Clear old logs (older than 30 days)
    /// </summary>
    [RelayCommand]
    private async Task ClearOldLogsAsync()
    {
        try
        {
            await _scanHistory.ClearOldLogsAsync(30);
            await RefreshAsync();

            _logger.LogInformation("Cleared old logs successfully");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to clear old logs: {ex.Message}";
            _logger.LogError(ex, "Failed to clear old logs");
        }
    }
}
