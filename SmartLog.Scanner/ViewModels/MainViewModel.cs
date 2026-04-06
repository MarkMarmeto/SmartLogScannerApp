using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;
using ZXing.Net.Maui;

namespace SmartLog.Scanner.ViewModels;

/// <summary>
/// US0007/US0008: ViewModel for main scanning page with visual feedback.
/// Handles both camera QR scanning and USB keyboard wedge input with modern UI state management.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly CameraQrScannerService? _cameraScanner;
    private readonly UsbQrScannerService? _usbScanner;
    private readonly IPreferencesService _preferences;
    private readonly ISoundService _soundService;
    private readonly IOfflineQueueService _offlineQueue;
    private readonly IHealthCheckService _healthCheck;
    private readonly IBackgroundSyncService _backgroundSync;
    private readonly ISecureConfigService _secureConfig;
    private readonly IScanHistoryService _scanHistory;
    private readonly ILogger<MainViewModel> _logger;
    private readonly string _scannerMode;

    // Scan state
    [ObservableProperty] private bool _isScanning = true;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _statusIcon = "";
    [ObservableProperty] private string _statusIconSource = "icon_camera.svg";

    // US0009: Scan type toggle (ENTRY/EXIT)
    [ObservableProperty] private string _currentScanType = "ENTRY";

    // Last scan result
    [ObservableProperty] private string? _lastStudentId;
    [ObservableProperty] private string? _lastScanTime;
    [ObservableProperty] private bool _lastScanValid;
    [ObservableProperty] private string? _lastScanMessage;

    // Student ID card data (populated on successful scan)
    [ObservableProperty] private string? _lastLrn;
    [ObservableProperty] private string? _lastStudentName;
    [ObservableProperty] private string? _lastGrade;
    [ObservableProperty] private string? _lastSection;
    [ObservableProperty] private bool _hasScannedStudent;
    [ObservableProperty] private Color _cardBorderColor = Color.FromArgb("#E0E0E0");

    // Computed: Grade & Section combined display
    public string? LastGradeSection =>
        !string.IsNullOrEmpty(LastGrade) && !string.IsNullOrEmpty(LastSection)
            ? $"{LastGrade} - {LastSection}"
            : LastGrade ?? LastSection;

    partial void OnLastGradeChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));
    partial void OnLastSectionChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));

    // Visual feedback colors
    [ObservableProperty] private Color _feedbackColor = Colors.Transparent;
    [ObservableProperty] private bool _showFeedback;

    // US0013: Statistics counters
    [ObservableProperty] private int _queuePendingCount;
    [ObservableProperty] private int _todayScanCount;

    // Offline queue visibility
    [ObservableProperty] private bool _hasQueuedScans;

    // US0015: Connectivity status indicator
    [ObservableProperty] private string _connectivityStatus = "Connecting...";
    [ObservableProperty] private string _connectivityIcon = "⚪";
    [ObservableProperty] private Color _connectivityColor = Color.FromArgb("#9E9E9E"); // Gray

    // Camera barcode reader options
    public BarcodeReaderOptions BarcodeReaderOptions { get; } = new BarcodeReaderOptions
    {
        Formats = BarcodeFormats.OneDimensional | BarcodeFormats.TwoDimensional,
        AutoRotate = true,
        Multiple = false
    };

    public MainViewModel(
        CameraQrScannerService cameraScanner,
        UsbQrScannerService usbScanner,
        IPreferencesService preferences,
        ISoundService soundService,
        IOfflineQueueService offlineQueue,
        IHealthCheckService healthCheck,
        IBackgroundSyncService backgroundSync,
        ISecureConfigService secureConfig,
        IScanHistoryService scanHistory,
        ILogger<MainViewModel> logger)
    {
        _cameraScanner = cameraScanner;
        _usbScanner = usbScanner;
        _preferences = preferences;
        _soundService = soundService;
        _offlineQueue = offlineQueue;
        _healthCheck = healthCheck;
        _backgroundSync = backgroundSync;
        _secureConfig = secureConfig;
        _scanHistory = scanHistory;
        _logger = logger;

        // Read scanner mode from preferences (set during setup)
        _scannerMode = Preferences.Get("Scanner.Mode", "Camera");

        // US0009: AC4 - Load saved scan type from preferences
        CurrentScanType = _preferences.GetDefaultScanType();

        // Subscribe to appropriate scanner based on mode
        if (_scannerMode == "Camera")
        {
            _cameraScanner.ScanCompleted += OnScanCompleted;
            StatusIcon = "📷";
            StatusIconSource = "icon_camera.svg";
        }
        else // USB
        {
            _usbScanner.ScanCompleted += OnScanCompleted;
            StatusIcon = "⌨️";
            StatusIconSource = "icon_keyboard.svg";
        }

        // US0015: Subscribe to connectivity changes
        _healthCheck.ConnectivityChanged += OnConnectivityChanged;

        // US0016: Subscribe to background sync completion to refresh queue count
        _backgroundSync.SyncCompleted += OnSyncCompleted;
    }

    public async Task InitializeAsync()
    {
        // US0012: Initialize audio service (pre-load sound files)
        await _soundService.InitializeAsync();

        // US0013: Initialize statistics counters
        await InitializeStatisticsAsync();

        // US0015: Start health check monitoring
        await _healthCheck.StartAsync();

        // BUGFIX: Initialize connectivity status from current health check state
        // (event only fires on changes, not on initial state)
        var currentStatus = _healthCheck.IsOnline;
        if (currentStatus == true)
        {
            ConnectivityStatus = "Online";
            ConnectivityIcon = "🟢";
            ConnectivityColor = Color.FromArgb("#4CAF50"); // Material Green
        }
        else if (currentStatus == false)
        {
            ConnectivityStatus = "Offline";
            ConnectivityIcon = "🔴";
            ConnectivityColor = Color.FromArgb("#F44336"); // Material Red
        }
        // else: null (connecting) - keep default

        if (_scannerMode == "Camera")
        {
            await _cameraScanner!.StartAsync();
            StatusMessage = "Ready to scan QR codes";
            StatusIcon = "📷";
            StatusIconSource = "icon_camera.svg";
        }
        else // USB
        {
            await _usbScanner!.StartAsync();
            StatusMessage = "Ready for USB scanner input";
            StatusIcon = "⌨️";
            StatusIconSource = "icon_keyboard.svg";
        }
        IsScanning = true;
    }


    private void OnScanCompleted(object? sender, ScanResult result)
    {
        // Log scan to history (async, don't block UI)
        _ = LogScanToHistoryAsync(result);

        // Update UI on main thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // US0010: Handle different scan statuses
            switch (result.Status)
            {
                case ScanStatus.Accepted:
                    // AC1: ACCEPTED - green feedback with student info
                    LastStudentId = result.StudentId;
                    LastLrn = result.Lrn;
                    LastStudentName = result.StudentName;
                    LastGrade = result.Grade;
                    LastSection = result.Section;
                    HasScannedStudent = true;
                    CardBorderColor = Color.FromArgb("#4CAF50"); // Green
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = $"✓ {result.StudentName ?? result.StudentId} - {result.Grade} {result.Section}";
                    FeedbackColor = Color.FromArgb("#4CAF50"); // Material Green
                    ShowFeedback = true;
                    StatusMessage = "Accepted!";
                    StatusIcon = "✓";
                    StatusIconSource = "icon_check.svg";
                    // US0012: Play success sound
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Accepted);
                    break;

                case ScanStatus.Duplicate:
                    // AC2: DUPLICATE - amber feedback
                    LastStudentId = result.StudentId;
                    LastLrn = result.Lrn;
                    LastStudentName = result.StudentName;
                    LastGrade = result.Grade;
                    LastSection = result.Section;
                    HasScannedStudent = !string.IsNullOrEmpty(result.StudentId);
                    CardBorderColor = Color.FromArgb("#FF9800"); // Amber
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = $"⚠ {result.Message ?? "Already scanned. Please proceed."}";
                    FeedbackColor = Color.FromArgb("#FF9800"); // Material Amber
                    ShowFeedback = true;
                    StatusMessage = "Duplicate scan";
                    StatusIcon = "⚠";
                    StatusIconSource = "icon_warning.svg";
                    // US0012: Play duplicate sound
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Duplicate);
                    break;

                case ScanStatus.Rejected:
                    // AC3: REJECTED - red feedback
                    LastStudentId = result.StudentId;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#F44336"); // Red
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = false;
                    LastScanMessage = $"✗ {result.Message ?? result.ValidationResult?.RejectionReason ?? "Rejected"}";
                    FeedbackColor = Color.FromArgb("#F44336"); // Material Red
                    ShowFeedback = true;
                    StatusMessage = "Rejected";
                    StatusIcon = "✗";
                    StatusIconSource = "icon_close.svg";
                    // US0012: Play error sound
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Rejected);
                    break;

                case ScanStatus.Queued:
                    // AC6: QUEUED - blue feedback (offline)
                    LastStudentId = result.StudentId;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#4D9B91"); // Teal
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = $"📥 {result.Message ?? "Scan queued (offline)"}";
                    FeedbackColor = Color.FromArgb("#4D9B91"); // Teal
                    ShowFeedback = true;
                    StatusMessage = "Queued offline";
                    StatusIcon = "📥";
                    StatusIconSource = "icon_queue.svg";
                    // US0012: Play queued sound
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Queued);
                    break;

                case ScanStatus.Error:
                    // AC4: ERROR - red feedback
                    LastStudentId = null;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#F44336"); // Red
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = false;
                    LastScanMessage = $"✗ {result.Message ?? "Error"}";
                    FeedbackColor = Color.FromArgb("#F44336"); // Material Red
                    ShowFeedback = true;
                    StatusMessage = "Error";
                    StatusIcon = "✗";
                    StatusIconSource = "icon_close.svg";
                    // US0012: Play error sound
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Error);
                    break;

                case ScanStatus.RateLimited:
                    // AC5: RATE LIMITED - amber feedback
                    LastStudentId = null;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#FF9800"); // Amber
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = false;
                    LastScanMessage = $"⏱ {result.Message ?? "Rate limit exceeded"}";
                    FeedbackColor = Color.FromArgb("#FF9800"); // Material Amber
                    ShowFeedback = true;
                    StatusMessage = "Rate limited";
                    StatusIcon = "⏱";
                    StatusIconSource = "icon_timer.svg";
                    // US0012: Play queued sound (same as offline)
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.RateLimited);
                    break;

                case ScanStatus.DebouncedLocally:
                    // DEBOUNCED LOCALLY - amber feedback (duplicate within warn window or already queued)
                    LastStudentId = result.StudentId;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#FF9800"); // Amber
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = $"⚠ {result.Message ?? "Already scanned. Please proceed."}";
                    FeedbackColor = Color.FromArgb("#FF9800"); // Material Amber
                    ShowFeedback = true;
                    StatusMessage = "Already scanned";
                    StatusIcon = "⚠";
                    StatusIconSource = "icon_warning.svg";
                    // Play duplicate sound
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Duplicate);
                    break;
            }

            // US0013: Update statistics counters
            _ = UpdateStatisticsAsync(result.Status);

            // Hide feedback after 3 seconds and reset card to skeleton
            Task.Delay(3000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ShowFeedback = false;
                    HasScannedStudent = false;
                    LastStudentId = null;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    CardBorderColor = Color.FromArgb("#E0E0E0");
                    if (_scannerMode == "Camera")
                    {
                        StatusMessage = "Ready to scan QR codes";
                        StatusIcon = "📷";
                        StatusIconSource = "icon_camera.svg";
                    }
                    else
                    {
                        StatusMessage = "Ready for USB scanner input";
                        StatusIcon = "⌨️";
                        StatusIconSource = "icon_keyboard.svg";
                    }
                });
            });
        });
    }

    /// <summary>
    /// Clear all pending scans from the queue (with confirmation).
    /// </summary>
    [RelayCommand]
    private async Task ClearQueue()
    {
        _logger.LogInformation("Clear queue triggered by user");
        System.Diagnostics.Debug.WriteLine("[MainViewModel] Clear queue button pressed");

        // Show confirmation dialog
        var count = QueuePendingCount;
        if (count == 0)
        {
            LastScanMessage = "ℹ️ Queue is already empty";
            FeedbackColor = Color.FromArgb("#4D9B91"); // Teal
            ShowFeedback = true;

            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ShowFeedback = false);
            });
            return;
        }

        var confirmed = await Application.Current!.MainPage!.DisplayAlert(
            "Clear Queue",
            $"Delete {count} pending scan{(count == 1 ? "" : "s")} from queue?\n\nThis cannot be undone.",
            "Clear",
            "Cancel");

        if (!confirmed)
        {
            _logger.LogInformation("Clear queue cancelled by user");
            return;
        }

        // Show clearing feedback
        LastScanMessage = "⏳ Clearing queue...";
        FeedbackColor = Color.FromArgb("#FF9800"); // Amber
        ShowFeedback = true;

        try
        {
            await _offlineQueue.ClearPendingScansAsync();
            QueuePendingCount = 0; // Update UI immediately

            _logger.LogInformation("Queue cleared successfully");

            // Show success feedback
            LastScanMessage = "✓ Queue cleared successfully";
            FeedbackColor = Color.FromArgb("#4CAF50"); // Green
            ShowFeedback = true;

            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ShowFeedback = false);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear queue");
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Clear queue failed: {ex.Message}");

            // Show error feedback
            LastScanMessage = $"✗ Failed to clear queue: {ex.Message}";
            FeedbackColor = Color.FromArgb("#F44336"); // Red
            ShowFeedback = true;

            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ShowFeedback = false);
            });
        }
    }

    /// <summary>
    /// Manual sync command for testing - triggers background sync immediately.
    /// </summary>
    [RelayCommand]
    private async Task ManualSync()
    {
        _logger.LogInformation("Manual sync triggered by user");
        System.Diagnostics.Debug.WriteLine("[MainViewModel] Manual sync button pressed");

        // Show syncing feedback
        LastScanMessage = "⏳ Syncing queued scans...";
        FeedbackColor = Color.FromArgb("#2196F3"); // Blue
        ShowFeedback = true;

        try
        {
            await _backgroundSync.TriggerSyncAsync();
            _logger.LogInformation("Manual sync completed");
            System.Diagnostics.Debug.WriteLine("[MainViewModel] Manual sync completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Manual sync failed: {ex.Message}");

            // Show error feedback
            LastScanMessage = $"✗ Sync failed: {ex.Message}";
            FeedbackColor = Color.FromArgb("#F44336"); // Red
            ShowFeedback = true;

            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ShowFeedback = false);
            });
        }
    }

    /// <summary>
    /// Test command to demonstrate valid QR scan feedback.
    /// SECURITY: Uses actual HMAC secret from secure storage (not hard-coded).
    /// </summary>
    [RelayCommand]
    private async Task TestValidQr()
    {
        // SECURITY: Retrieve HMAC secret from secure storage
        var secret = await _secureConfig.GetHmacSecretAsync();
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogWarning("Cannot generate test QR: HMAC secret not configured");
            LastScanMessage = "⚠️ HMAC secret not configured. Complete setup first.";
            FeedbackColor = Color.FromArgb("#FF9800"); // Amber
            ShowFeedback = true;

            // Auto-hide after 3 seconds
            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ShowFeedback = false);
            });
            return;
        }

        // Simulate valid QR: SMARTLOG:{studentId}:{timestamp}:{hmacBase64}
        var studentId = "STU12345";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var message = $"{studentId}:{timestamp}";

        // Compute HMAC using actual secret from secure storage
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(message));
        var hmacBase64 = Convert.ToBase64String(hash);

        var payload = $"SMARTLOG:{studentId}:{timestamp}:{hmacBase64}";

        if (_scannerMode == "Camera")
        {
            await _cameraScanner!.ProcessQrCodeAsync(payload);
        }
        else // USB
        {
            await _usbScanner!.ProcessQrCodeAsync(payload);
        }
    }

    /// <summary>
    /// Test command to demonstrate invalid QR scan feedback.
    /// </summary>
    [RelayCommand]
    private async Task TestInvalidQr()
    {
        // Simulate invalid QR (wrong HMAC)
        var payload = "SMARTLOG:STU99999:1234567890:aW52YWxpZC1obWFjLXNpZ25hdHVyZQ==";

        if (_scannerMode == "Camera")
        {
            await _cameraScanner!.ProcessQrCodeAsync(payload);
        }
        else // USB
        {
            await _usbScanner!.ProcessQrCodeAsync(payload);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Only stop the scanner, don't unsubscribe events.
        // Events stay subscribed because this ViewModel is a Singleton
        // and InitializeAsync/DisposeAsync are called on page appear/disappear.
        if (_scannerMode == "Camera")
        {
            await _cameraScanner!.StopAsync();
        }
        else // USB
        {
            await _usbScanner!.StopAsync();
        }
    }

    /// <summary>
    /// US0009: AC2/AC3 - Toggle scan type between ENTRY and EXIT.
    /// Persists change to preferences immediately.
    /// </summary>
    [RelayCommand]
    private void ToggleScanType()
    {
        CurrentScanType = CurrentScanType == "ENTRY" ? "EXIT" : "ENTRY";
        _preferences.SetDefaultScanType(CurrentScanType);
        _logger.LogInformation("Scan type toggled to: {ScanType}", CurrentScanType);
    }

    /// <summary>
    /// US0013: AC1/AC6 - Initialize statistics counters.
    /// </summary>
    private async Task InitializeStatisticsAsync()
    {
        // AC1: Get queue pending count
        try
        {
            QueuePendingCount = await _offlineQueue.GetQueueCountAsync();
            HasQueuedScans = QueuePendingCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queue count");
            QueuePendingCount = 0;
            HasQueuedScans = false;
        }

        // AC6: Check if we need to reset today's count (midnight rollover)
        var lastScanDate = Preferences.Get("Scanner.LastScanDate", string.Empty);
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        if (lastScanDate == today)
        {
            // Same day - load saved count
            TodayScanCount = Preferences.Get("Scanner.TodayScanCount", 0);
        }
        else
        {
            // New day - reset counter
            TodayScanCount = 0;
            Preferences.Set("Scanner.TodayScanCount", 0);
            Preferences.Set("Scanner.LastScanDate", today);
            _logger.LogInformation("New day detected - reset today's scan count");
        }
    }

    /// <summary>
    /// US0013: AC2/AC3/AC4/AC5 - Update statistics after scan.
    /// </summary>
    private async Task UpdateStatisticsAsync(ScanStatus status)
    {
        // AC2/AC3: Increment today's count for successful scans (online or queued)
        // Don't increment for duplicate or rejected scans
        if (status == ScanStatus.Accepted || status == ScanStatus.Queued)
        {
            TodayScanCount++;
            Preferences.Set("Scanner.TodayScanCount", TodayScanCount);

            // AC6: Update last scan date for midnight rollover detection
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            Preferences.Set("Scanner.LastScanDate", today);
        }

        // AC4/AC5: Update queue count
        try
        {
            QueuePendingCount = await _offlineQueue.GetQueueCountAsync();
            HasQueuedScans = QueuePendingCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update queue count");
        }
    }

    /// <summary>
    /// US0015: Handle connectivity state changes from health check service.
    /// Updates UI status indicator on main thread.
    /// </summary>
    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isOnline)
            {
                // US0015 AC5: Green dot + "Online"
                ConnectivityStatus = "Online";
                ConnectivityIcon = "🟢";
                ConnectivityColor = Color.FromArgb("#4CAF50"); // Material Green
                _logger.LogInformation("UI updated: Server is ONLINE");
            }
            else
            {
                // US0015 AC6: Red dot + "Offline"
                ConnectivityStatus = "Offline";
                ConnectivityIcon = "🔴";
                ConnectivityColor = Color.FromArgb("#F44336"); // Material Red
                _logger.LogInformation("UI updated: Server is OFFLINE");
            }
        });
    }

    /// <summary>
    /// US0016: Handle background sync completion to refresh queue count.
    /// Updates queue count on UI thread after scans are synced.
    /// </summary>
    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Refresh queue count from database
            try
            {
                QueuePendingCount = await _offlineQueue.GetQueueCountAsync();
                HasQueuedScans = QueuePendingCount > 0;
                _logger.LogInformation("Queue count refreshed after sync: {Count} pending (synced: {Synced}, failed: {Failed})",
                    QueuePendingCount, e.SyncedCount, e.FailedCount);

                // Show sync result feedback
                if (e.SyncedCount > 0 || e.FailedCount > 0 || e.SkippedCount > 0)
                {
                    var message = $"✓ Synced: {e.SyncedCount}";
                    if (e.FailedCount > 0) message += $" | ✗ Failed: {e.FailedCount}";
                    if (e.SkippedCount > 0) message += $" | ⏭ Skipped: {e.SkippedCount}";

                    // Add first error message if available
                    if (!string.IsNullOrEmpty(e.FirstErrorMessage))
                    {
                        message += $"\nError: {e.FirstErrorMessage}";
                    }

                    LastScanMessage = message;
                    FeedbackColor = e.SyncedCount > 0 ? Color.FromArgb("#4CAF50") : Color.FromArgb("#FF9800"); // Green or Amber
                    ShowFeedback = true;

                    _ = Task.Delay(8000).ContinueWith(_ =>
                    {
                        MainThread.BeginInvokeOnMainThread(() => ShowFeedback = false);
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh queue count after sync");
            }
        });
    }

    /// <summary>
    /// US0008: Process keystroke from USB keyboard wedge scanner.
    /// Called from MainPage keyboard event handler.
    /// </summary>
    public void ProcessKeystroke(string character)
    {
        if (_scannerMode == "USB")
        {
            _usbScanner?.ProcessKeystroke(character);
        }
    }

    /// <summary>
    /// US0008: Process Enter key from USB keyboard wedge scanner.
    /// Called from MainPage keyboard event handler.
    /// </summary>
    public void ProcessEnterKey()
    {
        if (_scannerMode == "USB")
        {
            _usbScanner?.ProcessEnterKey();
        }
    }

    /// <summary>
    /// US0007: Process QR code from camera barcode detection.
    /// Called from MainPage BarcodesDetected event handler.
    /// </summary>
    public async Task ProcessCameraQrCodeAsync(string payload)
    {
        if (_scannerMode == "Camera")
        {
            await _cameraScanner!.ProcessQrCodeAsync(payload);
        }
    }

    /// <summary>
    /// Log scan to history for diagnostics and troubleshooting
    /// </summary>
    private async Task LogScanToHistoryAsync(ScanResult result)
    {
        try
        {
            var logEntry = new ScanLogEntry
            {
                Timestamp = result.ScannedAt,
                RawPayload = result.RawPayload ?? string.Empty,
                StudentId = result.StudentId,
                StudentName = result.StudentName,
                ScanType = result.ScanType ?? CurrentScanType,
                Status = result.Status,
                Message = result.Message,
                ScanId = result.ScanId,
                NetworkAvailable = _healthCheck.IsOnline ?? false,
                ProcessingTimeMs = 0, // TODO: Track processing time if needed
                GradeSection = !string.IsNullOrEmpty(result.Grade) && !string.IsNullOrEmpty(result.Section)
                    ? $"{result.Grade} - {result.Section}"
                    : null,
                ErrorDetails = result.ErrorReason,
                ScanMethod = _scannerMode
            };

            await _scanHistory.LogScanAsync(logEntry);
        }
        catch (Exception ex)
        {
            // Don't let logging errors break scanning
            _logger.LogError(ex, "Failed to log scan to history");
        }
    }
}
