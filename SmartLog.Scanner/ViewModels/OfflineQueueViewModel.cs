using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.ViewModels;

/// <summary>
/// ViewModel for the offline queue management page.
/// Allows viewing, retrying, and clearing queued scans.
/// </summary>
public partial class OfflineQueueViewModel : ObservableObject
{
    private readonly IOfflineQueueService _offlineQueue;
    private readonly IBackgroundSyncService _backgroundSync;
    private readonly IHealthCheckService _healthCheck;
    private readonly ILogger<OfflineQueueViewModel> _logger;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _statusColorHex = "Transparent";
    [ObservableProperty] private bool _showStatus;

    // Counts
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _syncedCount;
    [ObservableProperty] private int _totalCount;

    // Connectivity
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private string _connectivityText = "Checking...";
    [ObservableProperty] private string _connectivityColorHex = "#9E9E9E";

    // Filter
    [ObservableProperty] private string _selectedFilter = "All";

    public ObservableCollection<QueuedScanDisplayItem> QueuedScans { get; } = new();

    public List<string> FilterOptions { get; } = new() { "All", "Pending", "Failed", "Synced" };

    public OfflineQueueViewModel(
        IOfflineQueueService offlineQueue,
        IBackgroundSyncService backgroundSync,
        IHealthCheckService healthCheck,
        ILogger<OfflineQueueViewModel> logger)
    {
        _offlineQueue = offlineQueue;
        _backgroundSync = backgroundSync;
        _healthCheck = healthCheck;
        _logger = logger;

        _healthCheck.ConnectivityChanged += OnConnectivityChanged;
        _backgroundSync.SyncCompleted += OnSyncCompleted;
    }

    public async Task InitializeAsync()
    {
        var online = _healthCheck.IsOnline;
        UpdateConnectivityDisplay(online == true);
        await LoadQueueAsync();
    }

    [RelayCommand]
    private async Task LoadQueueAsync()
    {
        IsLoading = true;
        try
        {
            var allScans = await _offlineQueue.GetAllScansAsync();

            PendingCount = allScans.Count(s => s.SyncStatus == "PENDING");
            FailedCount = allScans.Count(s => s.SyncStatus == "FAILED");
            SyncedCount = allScans.Count(s => s.SyncStatus == "SYNCED");
            TotalCount = allScans.Count;

            var filtered = SelectedFilter switch
            {
                "Pending" => allScans.Where(s => s.SyncStatus == "PENDING"),
                "Failed" => allScans.Where(s => s.SyncStatus == "FAILED"),
                "Synced" => allScans.Where(s => s.SyncStatus == "SYNCED"),
                _ => allScans.AsEnumerable()
            };

            QueuedScans.Clear();
            foreach (var scan in filtered)
            {
                QueuedScans.Add(new QueuedScanDisplayItem(scan));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load queue");
            ShowStatusMsg("Failed to load queue", "#F44336");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedFilterChanged(string value)
    {
        _ = LoadQueueAsync();
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        if (PendingCount == 0)
        {
            ShowStatusMsg("No pending scans to sync", "#FF9800");
            return;
        }

        if (_healthCheck.IsOnline != true)
        {
            ShowStatusMsg("Cannot sync: server is offline", "#F44336");
            return;
        }

        IsSyncing = true;
        ShowStatusMsg("Syncing queued scans...", "#2196F3");

        try
        {
            await _backgroundSync.TriggerSyncAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            ShowStatusMsg($"Sync failed: {ex.Message}", "#F44336");
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private async Task RetryFailed()
    {
        if (FailedCount == 0)
        {
            ShowStatusMsg("No failed scans to retry", "#FF9800");
            return;
        }

        try
        {
            var count = await _offlineQueue.RetryFailedScansAsync();
            ShowStatusMsg($"{count} failed scan{(count == 1 ? "" : "s")} reset for retry", "#4CAF50");
            await LoadQueueAsync();

            if (_healthCheck.IsOnline == true)
            {
                await _backgroundSync.TriggerSyncAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry failed scans");
            ShowStatusMsg($"Retry failed: {ex.Message}", "#F44336");
        }
    }

    [RelayCommand]
    private async Task ClearPending()
    {
        if (PendingCount == 0)
        {
            ShowStatusMsg("Queue is already empty", "#FF9800");
            return;
        }

        var confirmed = await Application.Current!.MainPage!.DisplayAlert(
            "Clear Pending Scans",
            $"Delete {PendingCount} pending scan{(PendingCount == 1 ? "" : "s")}?\n\nThese scans will NOT be submitted to the server. This cannot be undone.",
            "Clear",
            "Cancel");

        if (!confirmed) return;

        try
        {
            await _offlineQueue.ClearPendingScansAsync();
            ShowStatusMsg("Pending scans cleared", "#4CAF50");
            await LoadQueueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear pending scans");
            ShowStatusMsg($"Clear failed: {ex.Message}", "#F44336");
        }
    }

    [RelayCommand]
    private async Task ClearFailed()
    {
        if (FailedCount == 0)
        {
            ShowStatusMsg("No failed scans to clear", "#FF9800");
            return;
        }

        var confirmed = await Application.Current!.MainPage!.DisplayAlert(
            "Clear Failed Scans",
            $"Delete {FailedCount} permanently failed scan{(FailedCount == 1 ? "" : "s")}?\n\nThis cannot be undone.",
            "Clear",
            "Cancel");

        if (!confirmed) return;

        try
        {
            await _offlineQueue.ClearFailedScansAsync();
            ShowStatusMsg("Failed scans cleared", "#4CAF50");
            await LoadQueueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear failed scans");
            ShowStatusMsg($"Clear failed: {ex.Message}", "#F44336");
        }
    }

    [RelayCommand]
    private async Task DeleteScan(QueuedScanDisplayItem? item)
    {
        if (item == null) return;

        var confirmed = await Application.Current!.MainPage!.DisplayAlert(
            "Delete Scan",
            $"Delete this {item.SyncStatus.ToLower()} scan for student {item.StudentId}?",
            "Delete",
            "Cancel");

        if (!confirmed) return;

        try
        {
            await _offlineQueue.DeleteScanAsync(item.Id);
            QueuedScans.Remove(item);
            await RefreshCountsAsync();
            ShowStatusMsg("Scan deleted", "#4CAF50");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete scan {Id}", item.Id);
            ShowStatusMsg($"Delete failed: {ex.Message}", "#F44336");
        }
    }

    [RelayCommand]
    private async Task CleanupSynced()
    {
        if (SyncedCount == 0)
        {
            ShowStatusMsg("No synced scans to clean up", "#FF9800");
            return;
        }

        try
        {
            await _offlineQueue.DeleteSyncedScansAsync(0);
            ShowStatusMsg("Synced scan records cleaned up", "#4CAF50");
            await LoadQueueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup synced scans");
            ShowStatusMsg($"Cleanup failed: {ex.Message}", "#F44336");
        }
    }

    private async Task RefreshCountsAsync()
    {
        PendingCount = await _offlineQueue.GetQueueCountAsync();
        FailedCount = await _offlineQueue.GetFailedCountAsync();
        TotalCount = QueuedScans.Count;
        SyncedCount = TotalCount - PendingCount - FailedCount;
    }

    private void ShowStatusMsg(string message, string colorHex)
    {
        StatusMessage = message;
        StatusColorHex = colorHex;
        ShowStatus = true;

        _ = Task.Delay(5000).ContinueWith(_ =>
        {
            MainThread.BeginInvokeOnMainThread(() => ShowStatus = false);
        });
    }

    private void UpdateConnectivityDisplay(bool online)
    {
        IsOnline = online;
        ConnectivityText = online ? "Online" : "Offline";
        ConnectivityColorHex = online ? "#4CAF50" : "#F44336";
    }

    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() => UpdateConnectivityDisplay(isOnline));
    }

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            IsSyncing = false;

            var message = $"Sync complete: {e.SyncedCount} synced";
            if (e.FailedCount > 0) message += $", {e.FailedCount} failed";
            if (e.SkippedCount > 0) message += $", {e.SkippedCount} skipped";

            var colorHex = e.FailedCount > 0 ? "#FF9800" : "#4CAF50";
            ShowStatusMsg(message, colorHex);

            await LoadQueueAsync();
        });
    }
}

/// <summary>
/// Display wrapper for QueuedScan with computed UI properties.
/// </summary>
public class QueuedScanDisplayItem
{
    public int Id { get; }
    public string StudentId { get; }
    public string ScanType { get; }
    public string SyncStatus { get; }
    public int SyncAttempts { get; }
    public string? LastError { get; }
    public string CreatedAtDisplay { get; }
    public string ScannedAtDisplay { get; }
    public string StatusColorHex { get; }

    public QueuedScanDisplayItem(QueuedScan scan)
    {
        Id = scan.Id;
        StudentId = scan.StudentId;
        ScanType = scan.ScanType;
        SyncStatus = scan.SyncStatus;
        SyncAttempts = scan.SyncAttempts;
        LastError = scan.LastSyncError;

        if (DateTimeOffset.TryParse(scan.CreatedAt, out var created))
            CreatedAtDisplay = created.ToLocalTime().ToString("MMM dd HH:mm:ss");
        else
            CreatedAtDisplay = scan.CreatedAt;

        if (DateTimeOffset.TryParse(scan.ScannedAt, out var scanned))
            ScannedAtDisplay = scanned.ToLocalTime().ToString("MMM dd HH:mm:ss");
        else
            ScannedAtDisplay = scan.ScannedAt;

        StatusColorHex = scan.SyncStatus switch
        {
            "PENDING" => "#FF9800",
            "SYNCED" => "#4CAF50",
            "FAILED" => "#F44336",
            _ => "#9E9E9E"
        };
    }
}
