using System.Threading.Channels;
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
    private readonly ITimeService _timeService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly string _scannerMode;

    // Scan state
    [ObservableProperty] private bool _isScanning = true;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _statusIcon = "";

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

    // Optimistic scan tracking: timestamp of the last optimistic result still on screen
    private DateTimeOffset? _currentOptimisticScanAt;

    // Phase 2: Channel for decoupling camera detection from scan processing
    // Camera callback writes payload here; background consumer calls ProcessQrCodeAsync
    private readonly Channel<string> _scanChannel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(50)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private CancellationTokenSource? _channelCts;

    // US0015: Connectivity status indicator
    [ObservableProperty] private string _connectivityStatus = "Connecting...";
    [ObservableProperty] private string _connectivityIcon = "⚪";
    [ObservableProperty] private Color _connectivityColor = Color.FromArgb("#9E9E9E"); // Gray

    // Live clock display
    [ObservableProperty] private string _currentDateTime = string.Empty;
    private IDispatcherTimer? _clockTimer;

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
        ITimeService timeService,
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
        _timeService = timeService;
        _logger = logger;

        // Read scanner mode from preferences (set during setup)
        _scannerMode = Preferences.Get("Scanner.Mode", "Camera");

        // US0009: AC4 - Load saved scan type from preferences
        CurrentScanType = _preferences.GetDefaultScanType();

        // Subscribe to appropriate scanner based on mode
        if (_scannerMode == "Camera")
        {
            _cameraScanner.ScanCompleted += OnScanCompleted;
            _cameraScanner.ScanUpdated += OnScanUpdated;
            StatusIcon = "📷";
        }
        else // USB
        {
            _usbScanner.ScanCompleted += OnScanCompleted;
            StatusIcon = "⌨️";
        }

        // US0015: Subscribe to connectivity changes
        _healthCheck.ConnectivityChanged += OnConnectivityChanged;

        // US0016: Subscribe to background sync completion to refresh queue count
        _backgroundSync.SyncCompleted += OnSyncCompleted;
    }

    public async Task InitializeAsync()
    {
        // Sync clock with server before starting the ticker so the display reflects
        // the corrected time from the very first tick.
        await _timeService.SyncAsync();

        // Start live clock — uses ITimeService.UtcNow so the display shows synced time.
        // Converts to local time for display.
        CurrentDateTime = _timeService.UtcNow.ToLocalTime().ToString("ddd, MMM dd yyyy   hh:mm:ss tt");
        _clockTimer = Application.Current!.Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) =>
            CurrentDateTime = _timeService.UtcNow.ToLocalTime().ToString("ddd, MMM dd yyyy   hh:mm:ss tt");
        _clockTimer.Start();

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

            // Phase 2: Start background channel consumer so camera callback returns instantly
            _channelCts = new CancellationTokenSource();
            _ = Task.Run(() => ProcessScanChannelAsync(_channelCts.Token));
        }
        else // USB
        {
            await _usbScanner!.StartAsync();
            StatusMessage = "Ready for USB scanner input";
            StatusIcon = "⌨️";
        }
        IsScanning = true;
    }


    private void OnScanCompleted(object? sender, ScanResult result)
    {
        // For optimistic results, defer history/stats to OnScanUpdated (when server confirms).
        // For all other results (rejected, debounced, USB), log immediately.
        if (!result.IsOptimistic)
            _ = LogScanToHistoryAsync(result);

        // Update UI on main thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // US0010: Handle different scan statuses
            switch (result.Status)
            {
                case ScanStatus.Accepted:
                    // AC1: ACCEPTED - green feedback with student info (may be optimistic)
                    _currentOptimisticScanAt = result.IsOptimistic ? result.ScannedAt : null;
                    LastStudentId = result.StudentId;
                    LastLrn = result.Lrn;
                    LastStudentName = result.StudentName;
                    LastGrade = result.Grade;
                    LastSection = result.Section;
                    HasScannedStudent = true;
                    CardBorderColor = Color.FromArgb("#4CAF50"); // Green
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#4CAF50"); // Material Green
                    ShowFeedback = true;
                    StatusMessage = "Accepted!";
                    StatusIcon = "✓";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Accepted);
                    // Stats updated in OnScanUpdated for optimistic results
                    if (!result.IsOptimistic)
                        _ = UpdateStatisticsAsync(result.Status);
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
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#FF9800"); // Material Amber
                    ShowFeedback = true;
                    StatusMessage = "Already scanned";
                    StatusIcon = "⚠";
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
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#F44336"); // Material Red
                    ShowFeedback = true;
                    StatusMessage = "Rejected";
                    StatusIcon = "✗";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Rejected);
                    break;

                case ScanStatus.Queued:
                    // AC6: QUEUED - teal feedback (offline)
                    LastStudentId = result.StudentId;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#4D9B91"); // Teal
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#4D9B91"); // Teal
                    ShowFeedback = true;
                    StatusMessage = "Queued offline";
                    StatusIcon = "📥";
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
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#F44336"); // Material Red
                    ShowFeedback = true;
                    StatusMessage = "Error";
                    StatusIcon = "✗";
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
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#FF9800"); // Material Amber
                    ShowFeedback = true;
                    StatusMessage = "Rate limited";
                    StatusIcon = "⏱";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.RateLimited);
                    break;

                case ScanStatus.DebouncedLocally:
                    // DEBOUNCED LOCALLY - amber feedback
                    LastStudentId = result.StudentId;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#FF9800"); // Amber
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#FF9800"); // Material Amber
                    ShowFeedback = true;
                    StatusMessage = "Already scanned";
                    StatusIcon = "⚠";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Duplicate);
                    break;
            }

            // US0013: Update statistics counters (skipped for optimistic Accepted — handled in OnScanUpdated)
            if (result.Status != ScanStatus.Accepted || !result.IsOptimistic)
                _ = UpdateStatisticsAsync(result.Status);

            // Hide feedback after 3 seconds and reset card to skeleton.
            // 3s matches the CameraQrScannerService payload lockout window (PayloadLockoutWindow),
            // so the scanner is ready for a new card the moment feedback disappears.
            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _currentOptimisticScanAt = null; // No longer showing any optimistic result
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
                    }
                    else
                    {
                        StatusMessage = "Ready for USB scanner input";
                        StatusIcon = "⌨️";
                    }
                });
            });
        });
    }

    /// <summary>
    /// Handles server confirmation or correction of an optimistic camera scan result.
    /// Updates student info on screen, or flips feedback to red/amber if the server rejects.
    /// Also logs to history and updates statistics (deferred from OnScanCompleted for optimistic scans).
    /// </summary>
    private void OnScanUpdated(object? sender, ScanResult result)
    {
        // Log the real server result to history regardless of whether UI is still showing
        _ = LogScanToHistoryAsync(result);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Update statistics with the confirmed server status
            _ = UpdateStatisticsAsync(result.Status);

            // Only update the visual if we are still showing the optimistic card for this scan
            if (!ShowFeedback || _currentOptimisticScanAt != result.ScannedAt)
                return;

            _currentOptimisticScanAt = null; // Resolved

            switch (result.Status)
            {
                case ScanStatus.Accepted:
                    // Server confirmed — update card with full student info from server response
                    if (!string.IsNullOrEmpty(result.StudentName))
                    {
                        LastStudentName = result.StudentName;
                        LastLrn = result.Lrn;
                        LastGrade = result.Grade;
                        LastSection = result.Section;
                        LastScanMessage = ToFriendlyMessage(result);
                    }
                    break;

                case ScanStatus.Duplicate:
                    // Server says already scanned — flip to amber
                    HasScannedStudent = !string.IsNullOrEmpty(result.StudentId);
                    CardBorderColor = Color.FromArgb("#FF9800");
                    FeedbackColor = Color.FromArgb("#FF9800");
                    LastScanMessage = ToFriendlyMessage(result);
                    StatusMessage = "Already scanned";
                    StatusIcon = "⚠";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Duplicate);
                    break;

                case ScanStatus.Rejected:
                    // Server rejected (e.g. inactive student, not a school day) — flip to red
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#F44336");
                    FeedbackColor = Color.FromArgb("#F44336");
                    LastScanValid = false;
                    LastScanMessage = ToFriendlyMessage(result);
                    StatusMessage = "Rejected";
                    StatusIcon = "✗";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Rejected);
                    break;

                case ScanStatus.Error:
                    // Server unreachable — flip to red
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#F44336");
                    FeedbackColor = Color.FromArgb("#F44336");
                    LastScanValid = false;
                    LastScanMessage = ToFriendlyMessage(result);
                    StatusMessage = "Error";
                    StatusIcon = "✗";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Error);
                    break;
            }
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
        _clockTimer?.Stop();
        _clockTimer = null;

        // Only stop the scanner, don't unsubscribe events.
        // Events stay subscribed because this ViewModel is a Singleton
        // and InitializeAsync/DisposeAsync are called on page appear/disappear.
        if (_scannerMode == "Camera")
        {
            await _cameraScanner!.StopAsync();

            // Stop channel consumer; any items still in channel are discarded (page is leaving)
            _channelCts?.Cancel();
            _channelCts?.Dispose();
            _channelCts = null;
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
    /// Writes the payload to the scan channel and returns immediately so the camera callback
    /// is never blocked. The background channel consumer calls ProcessQrCodeAsync.
    /// </summary>
    public Task ProcessCameraQrCodeAsync(string payload)
    {
        if (_scannerMode == "Camera" && !string.IsNullOrEmpty(payload))
            _scanChannel.Writer.TryWrite(payload); // Non-blocking; drops oldest if channel full
        return Task.CompletedTask;
    }

    /// <summary>
    /// Background consumer that reads payloads from the scan channel and processes them.
    /// Runs for the lifetime of the scanning session (started in InitializeAsync).
    /// </summary>
    private async Task ProcessScanChannelAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var payload in _scanChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    if (_cameraScanner != null)
                        await _cameraScanner.ProcessQrCodeAsync(payload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing scan payload from channel");
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    /// <summary>
    /// Log scan to history for diagnostics and troubleshooting.
    /// Always records the full technical detail regardless of what is shown in the UI.
    /// </summary>
    private async Task LogScanToHistoryAsync(ScanResult result)
    {
        try
        {
            // Processing time = wall-clock time from scan capture to result received
            var processingTimeMs = result.ScannedAt != default
                ? (long)(DateTimeOffset.UtcNow - result.ScannedAt).TotalMilliseconds
                : 0L;

            // Full technical detail for ErrorDetails — useful for support/debugging
            var technicalDetail = BuildTechnicalDetail(result);

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
                ProcessingTimeMs = processingTimeMs,
                GradeSection = !string.IsNullOrEmpty(result.Grade) && !string.IsNullOrEmpty(result.Section)
                    ? $"{result.Grade} - {result.Section}"
                    : null,
                ErrorDetails = technicalDetail,
                ScanMethod = _scannerMode
            };

            await _scanHistory.LogScanAsync(logEntry);

            // Structured log for operational monitoring (technical detail always logged, never shown in UI)
            _logger.LogInformation(
                "Scan logged: Status={Status} StudentId={StudentId} StudentName={StudentName} " +
                "GradeSection={GradeSection} ScanType={ScanType} ScanMethod={ScanMethod} " +
                "ProcessingMs={ProcessingMs} Network={Network} Detail={Detail}",
                result.Status,
                result.StudentId ?? "(unknown)",
                result.StudentName ?? "(unknown)",
                logEntry.GradeSection ?? "(unknown)",
                logEntry.ScanType,
                _scannerMode,
                processingTimeMs,
                _healthCheck.IsOnline,
                technicalDetail);
        }
        catch (Exception ex)
        {
            // Don't let logging errors break scanning
            _logger.LogError(ex, "Failed to log scan to history");
        }
    }

    /// <summary>
    /// Builds a full technical detail string for logging — includes error codes, HMAC rejection
    /// reasons, and server messages. Never displayed in the UI.
    /// </summary>
    private static string BuildTechnicalDetail(ScanResult result)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(result.ErrorReason))
            parts.Add($"ErrorReason={result.ErrorReason}");

        var hmacReason = result.ValidationResult?.RejectionReason;
        if (!string.IsNullOrEmpty(hmacReason))
            parts.Add($"HmacReason={hmacReason}");

        if (!string.IsNullOrEmpty(result.Message))
            parts.Add($"ServerMessage={result.Message}");

        if (result.RetryAfterSeconds.HasValue)
            parts.Add($"RetryAfter={result.RetryAfterSeconds}s");

        return parts.Count > 0 ? string.Join(" | ", parts) : string.Empty;
    }

    /// <summary>
    /// Converts a ScanResult into a user-friendly display message.
    /// Technical error codes and HMAC reasons are translated to plain language here;
    /// the raw technical detail is preserved in logs only.
    /// </summary>
    private static string ToFriendlyMessage(ScanResult result)
    {
        return result.Status switch
        {
            ScanStatus.Accepted =>
                $"✓ {result.StudentName ?? result.StudentId} — {result.Grade} {result.Section}".TrimEnd(' ', '—'),

            ScanStatus.Duplicate =>
                $"⚠ {result.Message ?? "Already scanned — please proceed"}",

            ScanStatus.DebouncedLocally =>
                $"⚠ {result.Message ?? "Already scanned — please proceed"}",

            ScanStatus.Queued =>
                $"📥 {result.Message ?? "Scan saved — will sync when online"}",

            ScanStatus.RateLimited =>
                result.RetryAfterSeconds.HasValue
                    ? $"⏱ Too many scans — please wait {result.RetryAfterSeconds}s"
                    : "⏱ Too many scans — please wait a moment",

            ScanStatus.Rejected => ToFriendlyRejectedMessage(result),

            ScanStatus.Error => ToFriendlyErrorMessage(result),

            _ => result.Message ?? "Unknown status"
        };
    }

    private static string ToFriendlyRejectedMessage(ScanResult result)
    {
        // Server rejection messages are already user-friendly (come from the web app)
        // Pass them through directly if they don't look like raw codes
        var serverMessage = result.Message;
        if (!string.IsNullOrEmpty(serverMessage) &&
            !serverMessage.Equals("QR code rejected by server.", StringComparison.OrdinalIgnoreCase))
        {
            return $"✗ {serverMessage}";
        }

        // HMAC/local validation failures — translate technical codes to plain language
        var reason = result.ValidationResult?.RejectionReason ?? string.Empty;

        if (reason.StartsWith("Expired", StringComparison.Ordinal))
            return "✗ QR code expired — student needs a new ID card";

        if (reason.StartsWith("SecretUnavailable", StringComparison.Ordinal))
            return "✗ Device not configured — contact IT support";

        // Malformed, InvalidPrefix, InvalidBase64, InvalidSignature → all map to the same thing
        return "✗ Invalid QR code";
    }

    private static string ToFriendlyErrorMessage(ScanResult result)
    {
        var errorReason = result.ErrorReason ?? string.Empty;

        // Setup errors — keep existing message (already user-friendly)
        if (errorReason is "MissingApiKey" or "MissingServerUrl")
            return $"✗ {result.Message}";

        if (errorReason == "InvalidApiKey")
            return "✗ Device not authorised — contact IT support";

        if (errorReason == "NetworkError")
            return "✗ No connection — check your network";

        if (errorReason == "Cancelled")
            return "✗ Scan cancelled";

        // All other server/response errors
        return "✗ Something went wrong — please try again";
    }
}
