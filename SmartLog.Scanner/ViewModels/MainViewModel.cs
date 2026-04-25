using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.ViewModels;

/// <summary>
/// US0007/US0008/EP0011: ViewModel for main scanning page.
/// Handles multi-camera QR scanning and USB keyboard-wedge input with visual feedback.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IMultiCameraManager _multiCameraManager;
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
    [ObservableProperty] private string? _lastProgram;
    [ObservableProperty] private bool _hasScannedStudent;
    [ObservableProperty] private Color _cardBorderColor = Color.FromArgb("#E0E0E0");

    // EP0011: Camera that produced the most recent scan
    [ObservableProperty] private string? _lastScanCameraName;

    // Computed: Grade · Program · Section (Program omitted when null/empty)
    public string? LastGradeSection
    {
        get
        {
            if (string.IsNullOrEmpty(LastGrade) && string.IsNullOrEmpty(LastSection))
                return null;
            var grade = LastGrade ?? string.Empty;
            var section = LastSection ?? string.Empty;
            return string.IsNullOrEmpty(LastProgram)
                ? $"{grade} · {section}".Trim(' ', '·')
                : $"{grade} · {LastProgram} · {section}".Trim(' ', '·');
        }
    }

    partial void OnLastGradeChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));
    partial void OnLastSectionChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));
    partial void OnLastProgramChanged(string? value) => OnPropertyChanged(nameof(LastGradeSection));

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

    // US0015: Connectivity status indicator
    [ObservableProperty] private string _connectivityStatus = "Connecting...";
    [ObservableProperty] private string _connectivityIcon = "⚪";
    [ObservableProperty] private Color _connectivityColor = Color.FromArgb("#9E9E9E"); // Gray

    // Live clock display (US0092: split into time + date for two-line header)
    [ObservableProperty] private string _currentTime = string.Empty;
    [ObservableProperty] private string _currentDate = string.Empty;
    private IDispatcherTimer? _clockTimer;

    // Selected camera device ID (single-camera legacy; multi-camera uses CameraSlots)
    [ObservableProperty] private string _selectedCameraId = string.Empty;

    // EP0011: True when the scanner is in Camera mode (shows camera grid).
    public bool IsCameraMode => _scannerMode == "Camera";

    // EP0011: Fixed 8-slot observable collection. Slots beyond configured count have IsVisible=false.
    // Initialized in constructor (after DI) so RestartCommand callbacks can reference _multiCameraManager.
    public ObservableCollection<CameraSlotState> CameraSlots { get; }

    // EP0011: Per-slot flash animation cancellation tokens (prevent timer leaks on rapid scans)
    private readonly Dictionary<int, CancellationTokenSource> _flashTimers = new();

    // EP0011: 1-second timer for per-slot frame rate display
    private IDispatcherTimer? _frameRateTimer;

    public MainViewModel(
        IMultiCameraManager multiCameraManager,
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
        _multiCameraManager = multiCameraManager;
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

        // EP0011: Initialize camera slots with restart callback wired to MultiCameraManager
        CameraSlots = new ObservableCollection<CameraSlotState>(
            Enumerable.Range(0, 8).Select(i => new CameraSlotState(
                index: i,
                restartCallback: async idx => await _multiCameraManager.RestartCameraAsync(idx))));

        // US0009: AC4 - Load saved scan type from preferences
        CurrentScanType = _preferences.GetDefaultScanType();

        if (_scannerMode == "Camera")
        {
            // EP0011: Subscribe to multi-camera events
            _multiCameraManager.ScanCompleted += OnMultiCameraScanCompleted;
            _multiCameraManager.ScanUpdated += OnMultiCameraScanUpdated;
            _multiCameraManager.CameraStatusChanged += OnMultiCameraStatusChanged;
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
        // Refresh selected camera ID (legacy single-camera; multi-camera reads from preferences below)
        SelectedCameraId = _preferences.GetSelectedCameraId();

        // Sync clock with server before starting the ticker so the display reflects
        // the corrected time from the very first tick.
        await _timeService.SyncAsync();

        UpdateClock();
        _clockTimer = Application.Current!.Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        // US0012: Initialize audio service (pre-load sound files)
        await _soundService.InitializeAsync();

        // US0013: Initialize statistics counters
        await InitializeStatisticsAsync();

        // US0015: Start health check monitoring
        await _healthCheck.StartAsync();

        // BUGFIX: Initialize connectivity status from current health check state
        var currentStatus = _healthCheck.IsOnline;
        if (currentStatus == true)
        {
            ConnectivityStatus = "Online";
            ConnectivityIcon = "🟢";
            ConnectivityColor = Color.FromArgb("#4CAF50");
        }
        else if (currentStatus == false)
        {
            ConnectivityStatus = "Offline";
            ConnectivityIcon = "🔴";
            ConnectivityColor = Color.FromArgb("#F44336");
        }

        if (_scannerMode == "Camera")
        {
            // EP0011: Build CameraInstance list from preferences and hand to manager
            var cameraCount = Math.Clamp(_preferences.GetCameraCount(), 1, 8);
            var cameraConfigs = BuildCameraConfigs(cameraCount);

            // Apply config to the observable slot states (drives CameraQrView bindings)
            ApplyCameraConfigsToSlots(cameraConfigs, cameraCount);

            await _multiCameraManager.InitializeAsync(cameraConfigs);
            await _multiCameraManager.StartAllAsync();

            // EP0011: 1-second timer — calls UpdateFrameRate on every visible slot
            _frameRateTimer = Application.Current!.Dispatcher.CreateTimer();
            _frameRateTimer.Interval = TimeSpan.FromSeconds(1);
            _frameRateTimer.Tick += OnFrameRateTick;
            _frameRateTimer.Start();

            StatusMessage = "Ready to scan QR codes";
            StatusIcon = "📷";
        }
        else // USB
        {
            await _usbScanner!.StartAsync();
            StatusMessage = "Ready for USB scanner input";
            StatusIcon = "⌨️";
        }
        IsScanning = true;
    }

    /// <summary>
    /// EP0011: Builds CameraInstance list from per-camera preferences.
    /// For camera 0 on existing installs, falls back to the legacy SelectedCameraId
    /// if the multi-camera preference was never set.
    /// </summary>
    private List<CameraInstance> BuildCameraConfigs(int count)
    {
        var scanType = _preferences.GetDefaultScanType();
        var configs = new List<CameraInstance>();
        for (var i = 0; i < count; i++)
        {
            var deviceId = _preferences.GetCameraDeviceId(i);

            // Fallback for existing single-camera installs: camera 0 uses legacy preference
            if (string.IsNullOrEmpty(deviceId) && i == 0)
                deviceId = _preferences.GetSelectedCameraId();

            configs.Add(new CameraInstance
            {
                Index = i,
                CameraDeviceId = deviceId,
                DisplayName = _preferences.GetCameraName(i),
                ScanType = scanType,
                IsEnabled = _preferences.GetCameraEnabled(i),
                DecodeThrottleFrames = AdaptiveDecodeThrottle.Calculate(count)
            });
        }
        return configs;
    }

    /// <summary>
    /// EP0011: Applies camera config list to the 8 observable slots that drive CameraQrView bindings.
    /// Slots beyond count have IsVisible = false.
    /// </summary>
    private void ApplyCameraConfigsToSlots(IReadOnlyList<CameraInstance> configs, int count)
    {
        for (var i = 0; i < 8; i++)
        {
            var slot = CameraSlots[i];
            if (i < count)
            {
                var cfg = configs[i];
                slot.DisplayName = cfg.DisplayName;
                slot.ScanType = cfg.ScanType;
                slot.CameraDeviceId = cfg.CameraDeviceId;
                slot.IsVisible = cfg.IsEnabled;
                slot.Status = CameraStatus.Idle;
            }
            else
            {
                slot.IsVisible = false;
            }
        }
    }

    // ── EP0011: Multi-camera event handlers ─────────────────────────────────

    /// <summary>
    /// Routes a multi-camera ScanCompleted event: updates the source slot's flash,
    /// records which camera fired, then delegates to the shared scan result display logic.
    /// </summary>
    private void OnMultiCameraScanCompleted(object? sender, (int CameraIndex, ScanResult Result) e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (e.CameraIndex >= 0 && e.CameraIndex < CameraSlots.Count)
            {
                var slot = CameraSlots[e.CameraIndex];
                LastScanCameraName = slot.DisplayName;

                // Flash animation for the source camera cell
                if (e.Result.Status == ScanStatus.Accepted)
                {
                    var flashName = e.Result.IsVisitorScan
                        ? $"Visitor #{e.Result.PassNumber} — {e.Result.ScanType}"
                        : e.Result.StudentName ?? e.Result.StudentId;
                    TriggerSlotFlash(e.CameraIndex, flashName);
                }
            }
        });

        OnScanCompleted(sender, e.Result);
    }

    /// <summary>
    /// Routes a multi-camera ScanUpdated event to the shared update display logic.
    /// </summary>
    private void OnMultiCameraScanUpdated(object? sender, (int CameraIndex, ScanResult Result) e)
    {
        OnScanUpdated(sender, e.Result);
    }

    /// <summary>
    /// Updates the CameraSlotState when a camera's runtime status changes.
    /// </summary>
    private void OnMultiCameraStatusChanged(object? sender, (int CameraIndex, CameraStatus Status) e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (e.CameraIndex >= 0 && e.CameraIndex < CameraSlots.Count)
            {
                var slot = CameraSlots[e.CameraIndex];
                slot.Status = e.Status;

                _logger.LogDebug("Camera {Index} status → {Status}", e.CameraIndex, e.Status);
            }
        });
    }

    // ── EP0011: Per-slot flash animation ────────────────────────────────────

    /// <summary>
    /// Triggers a 1.5s scan-flash animation on the specified camera cell.
    /// Cancels any in-progress flash for the same slot to prevent timer leaks.
    /// </summary>
    private void TriggerSlotFlash(int cameraIndex, string? studentName)
    {
        if (cameraIndex < 0 || cameraIndex >= CameraSlots.Count) return;

        // Cancel previous flash timer for this slot (prevents stale clear)
        if (_flashTimers.TryGetValue(cameraIndex, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        var cts = new CancellationTokenSource();
        _flashTimers[cameraIndex] = cts;

        var slot = CameraSlots[cameraIndex];
        slot.FlashStudentName = studentName;
        slot.ShowFlash = true;

        _ = Task.Delay(1500, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                slot.ShowFlash = false;
                slot.FlashStudentName = null;
            });
        });
    }

    // ── EP0011: Frame rate timer ─────────────────────────────────────────────

    private void OnFrameRateTick(object? sender, EventArgs e)
    {
        foreach (var slot in CameraSlots)
            slot.UpdateFrameRate();
    }

    // ── EP0011: Public methods for MainPage code-behind ──────────────────────

    /// <summary>
    /// Routes a barcode detected by a specific CameraQrView to the multi-camera manager.
    /// Called from MainPage.xaml.cs BarcodeDetected event handlers.
    /// </summary>
    public Task OnBarcodeFromCameraAsync(int cameraIndex, string payload)
    {
        if (_scannerMode == "Camera" && !string.IsNullOrEmpty(payload))
            return _multiCameraManager.ProcessQrCodeAsync(cameraIndex, payload);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops all cameras — called from Window.Destroying and OnDisappearing lifecycle events.
    /// </summary>
    public async Task StopCamerasAsync()
    {
        if (_scannerMode == "Camera")
            await _multiCameraManager.StopAllAsync();
    }

    /// <summary>
    /// EP0011: Passes the worker for camera 0 (or any index) to the supplied action.
    /// Used by MainPage to attach a platform preview layer without coupling Core to platform types.
    /// </summary>
    public void ConfigureCameraPreview(int cameraIndex, Action<ICameraWorker> configure)
        => _multiCameraManager.ConfigureCameraPreview(cameraIndex, configure);

    // ── EP0011: RestartCamera command ────────────────────────────────────────

    /// <summary>
    /// Relay command bound to the per-cell Restart button (shown when CanRestart = true).
    /// </summary>
    [RelayCommand]
    private async Task RestartCamera(int cameraIndex)
    {
        _logger.LogInformation("User requested restart for camera {Index}", cameraIndex);
        await _multiCameraManager.RestartCameraAsync(cameraIndex);
    }

    // ── Shared scan result display logic ────────────────────────────────────

    private void OnScanCompleted(object? sender, ScanResult result)
    {
        // For optimistic results, defer history/stats to OnScanUpdated (when server confirms).
        if (!result.IsOptimistic)
            _ = LogScanToHistoryAsync(result);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (result.Status)
            {
                case ScanStatus.Accepted:
                    _currentOptimisticScanAt = result.IsOptimistic ? result.ScannedAt : null;
                    LastStudentId = result.StudentId;
                    LastLrn = result.IsVisitorScan ? null : result.Lrn;
                    LastStudentName = result.IsVisitorScan
                        ? $"Visitor Pass #{result.PassNumber}"
                        : result.StudentName;
                    LastGrade = result.IsVisitorScan ? null : result.Grade;
                    LastSection = result.IsVisitorScan ? null : result.Section;
                    LastProgram = result.IsVisitorScan ? null : result.Program;
                    HasScannedStudent = true;
                    // US0076-AC2: Blue for visitors, green for students
                    CardBorderColor = result.IsVisitorScan
                        ? Color.FromArgb("#2196F3")
                        : Color.FromArgb("#4CAF50");
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = result.IsVisitorScan
                        ? Color.FromArgb("#2196F3")
                        : Color.FromArgb("#4CAF50");
                    ShowFeedback = true;
                    StatusMessage = "Accepted!";
                    StatusIcon = "✓";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Accepted);
                    if (!result.IsOptimistic)
                        _ = UpdateStatisticsAsync(result.Status);
                    break;

                case ScanStatus.Duplicate:
                    LastStudentId = result.StudentId;
                    LastLrn = result.IsVisitorScan ? null : result.Lrn;
                    LastStudentName = result.IsVisitorScan
                        ? $"Visitor Pass #{result.PassNumber}"
                        : result.StudentName;
                    LastGrade = result.IsVisitorScan ? null : result.Grade;
                    LastSection = result.IsVisitorScan ? null : result.Section;
                    LastProgram = result.IsVisitorScan ? null : result.Program;
                    HasScannedStudent = result.IsVisitorScan || !string.IsNullOrEmpty(result.StudentId);
                    CardBorderColor = Color.FromArgb("#FF9800");
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#FF9800");
                    ShowFeedback = true;
                    StatusMessage = "Already scanned";
                    StatusIcon = "⚠";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Duplicate);
                    break;

                case ScanStatus.Rejected:
                    LastStudentId = result.StudentId;
                    LastStudentName = result.IsVisitorScan
                        ? $"Visitor Pass #{result.PassNumber}"
                        : null;
                    LastGrade = null;
                    LastSection = null;
                    LastProgram = null;
                    HasScannedStudent = result.IsVisitorScan;
                    CardBorderColor = Color.FromArgb("#F44336");
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = false;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#F44336");
                    ShowFeedback = true;
                    StatusMessage = result.IsVisitorScan ? "Pass Inactive" : "Rejected";
                    StatusIcon = "✗";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Rejected);
                    break;

                case ScanStatus.Queued:
                    LastStudentId = result.StudentId;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    LastProgram = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#4D9B91");
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#4D9B91");
                    ShowFeedback = true;
                    StatusMessage = "Queued offline";
                    StatusIcon = "📥";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Queued);
                    break;

                case ScanStatus.Error:
                    LastStudentId = null;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    LastProgram = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#F44336");
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = false;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#F44336");
                    ShowFeedback = true;
                    StatusMessage = "Error";
                    StatusIcon = "✗";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Error);
                    break;

                case ScanStatus.RateLimited:
                    LastStudentId = null;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    LastProgram = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#FF9800");
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = false;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#FF9800");
                    ShowFeedback = true;
                    StatusMessage = "Rate limited";
                    StatusIcon = "⏱";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.RateLimited);
                    break;

                case ScanStatus.DebouncedLocally:
                    LastStudentId = result.StudentId;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    LastProgram = null;
                    HasScannedStudent = false;
                    CardBorderColor = Color.FromArgb("#FF9800");
                    LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
                    LastScanValid = true;
                    LastScanMessage = ToFriendlyMessage(result);
                    FeedbackColor = Color.FromArgb("#FF9800");
                    ShowFeedback = true;
                    StatusMessage = "Already scanned";
                    StatusIcon = "⚠";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Duplicate);
                    break;
            }

            // US0013: Update statistics (skipped for optimistic Accepted — handled in OnScanUpdated)
            if (result.Status != ScanStatus.Accepted || !result.IsOptimistic)
                _ = UpdateStatisticsAsync(result.Status);

            // Hide feedback after 3 seconds and reset card to skeleton
            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _currentOptimisticScanAt = null;
                    ShowFeedback = false;
                    HasScannedStudent = false;
                    LastStudentId = null;
                    LastStudentName = null;
                    LastGrade = null;
                    LastSection = null;
                    LastProgram = null;
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
    /// </summary>
    private void OnScanUpdated(object? sender, ScanResult result)
    {
        _ = LogScanToHistoryAsync(result);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = UpdateStatisticsAsync(result.Status);

            if (!ShowFeedback || _currentOptimisticScanAt != result.ScannedAt)
                return;

            _currentOptimisticScanAt = null;

            switch (result.Status)
            {
                case ScanStatus.Accepted:
                    if (!string.IsNullOrEmpty(result.StudentName))
                    {
                        LastStudentName = result.StudentName;
                        LastLrn = result.Lrn;
                        LastGrade = result.Grade;
                        LastSection = result.Section;
                        LastProgram = result.Program;
                        LastScanMessage = ToFriendlyMessage(result);
                    }
                    break;

                case ScanStatus.Duplicate:
                    HasScannedStudent = !string.IsNullOrEmpty(result.StudentId);
                    CardBorderColor = Color.FromArgb("#FF9800");
                    FeedbackColor = Color.FromArgb("#FF9800");
                    LastScanMessage = ToFriendlyMessage(result);
                    StatusMessage = "Already scanned";
                    StatusIcon = "⚠";
                    _ = _soundService.PlayResultSoundAsync(ScanStatus.Duplicate);
                    break;

                case ScanStatus.Rejected:
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

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Clear all pending scans from the queue (with confirmation).
    /// </summary>
    [RelayCommand]
    private async Task ClearQueue()
    {
        _logger.LogInformation("Clear queue triggered by user");

        var count = QueuePendingCount;
        if (count == 0)
        {
            LastScanMessage = "ℹ️ Queue is already empty";
            FeedbackColor = Color.FromArgb("#4D9B91");
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

        if (!confirmed) return;

        LastScanMessage = "⏳ Clearing queue...";
        FeedbackColor = Color.FromArgb("#FF9800");
        ShowFeedback = true;

        try
        {
            await _offlineQueue.ClearPendingScansAsync();
            QueuePendingCount = 0;

            LastScanMessage = "✓ Queue cleared successfully";
            FeedbackColor = Color.FromArgb("#4CAF50");
            ShowFeedback = true;

            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ShowFeedback = false);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear queue");
            LastScanMessage = $"✗ Failed to clear queue: {ex.Message}";
            FeedbackColor = Color.FromArgb("#F44336");
            ShowFeedback = true;

            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ShowFeedback = false);
            });
        }
    }

    /// <summary>
    /// Manual sync command — triggers background sync immediately.
    /// </summary>
    [RelayCommand]
    private async Task ManualSync()
    {
        _logger.LogInformation("Manual sync triggered by user");

        LastScanMessage = "⏳ Syncing queued scans...";
        FeedbackColor = Color.FromArgb("#2196F3");
        ShowFeedback = true;

        try
        {
            await _backgroundSync.TriggerSyncAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            LastScanMessage = $"✗ Sync failed: {ex.Message}";
            FeedbackColor = Color.FromArgb("#F44336");
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
        var secret = await _secureConfig.GetHmacSecretAsync();
        if (string.IsNullOrEmpty(secret))
        {
            LastScanMessage = "⚠️ HMAC secret not configured. Complete setup first.";
            FeedbackColor = Color.FromArgb("#FF9800");
            ShowFeedback = true;
            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ShowFeedback = false);
            });
            return;
        }

        var studentId = "STU12345";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var message = $"{studentId}:{timestamp}";

        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(message));
        var hmacBase64 = Convert.ToBase64String(hash);
        var payload = $"SMARTLOG:{studentId}:{timestamp}:{hmacBase64}";

        if (_scannerMode == "Camera")
            await _multiCameraManager.ProcessQrCodeAsync(0, payload);
        else
            await _usbScanner!.ProcessQrCodeAsync(payload);
    }

    /// <summary>
    /// Test command to demonstrate invalid QR scan feedback.
    /// </summary>
    [RelayCommand]
    private async Task TestInvalidQr()
    {
        var payload = "SMARTLOG:STU99999:1234567890:aW52YWxpZC1obWFjLXNpZ25hdHVyZQ==";

        if (_scannerMode == "Camera")
            await _multiCameraManager.ProcessQrCodeAsync(0, payload);
        else
            await _usbScanner!.ProcessQrCodeAsync(payload);
    }

    private void UpdateClock()
    {
        var now = _timeService.UtcNow.ToLocalTime();
        CurrentTime = now.ToString("HH:mm");
        CurrentDate = now.ToString("ddd, dd MMM yyyy");
    }

    public async ValueTask DisposeAsync()
    {
        _clockTimer?.Stop();
        _clockTimer = null;

        _frameRateTimer?.Stop();
        _frameRateTimer = null;

        // Cancel all in-progress flash timers
        foreach (var cts in _flashTimers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _flashTimers.Clear();

        if (_scannerMode == "Camera")
        {
            await _multiCameraManager.StopAllAsync();
        }
        else // USB
        {
            await _usbScanner!.StopAsync();
        }
    }

    /// <summary>
    /// US0009: AC2/AC3 - Toggle scan type between ENTRY and EXIT.
    /// Persists device-level change to preferences and propagates to all running cameras immediately.
    /// </summary>
    [RelayCommand]
    private void ToggleScanType()
    {
        CurrentScanType = CurrentScanType == "ENTRY" ? "EXIT" : "ENTRY";
        _preferences.SetDefaultScanType(CurrentScanType);

        if (_scannerMode == "Camera")
        {
            // Propagate to running CameraQrScannerService instances
            _multiCameraManager.UpdateScanTypes(CurrentScanType);

            // Sync the observable CameraSlots so the status card badges update immediately
            foreach (var cam in _multiCameraManager.Cameras)
            {
                if (cam.Index >= 0 && cam.Index < CameraSlots.Count)
                    CameraSlots[cam.Index].ScanType = cam.ScanType;
            }
        }

        _logger.LogInformation("Scan type toggled to: {ScanType}", CurrentScanType);
    }

    // ── USB keyboard wedge methods ───────────────────────────────────────────

    /// <summary>
    /// US0008: Process keystroke from USB keyboard wedge scanner.
    /// </summary>
    public void ProcessKeystroke(string character)
    {
        if (_scannerMode == "USB")
            _usbScanner?.ProcessKeystroke(character);
    }

    /// <summary>
    /// US0008: Process Enter key from USB keyboard wedge scanner.
    /// </summary>
    public void ProcessEnterKey()
    {
        if (_scannerMode == "USB")
            _usbScanner?.ProcessEnterKey();
    }

    // ── Statistics helpers ───────────────────────────────────────────────────

    private async Task InitializeStatisticsAsync()
    {
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

        var lastScanDate = Preferences.Get("Scanner.LastScanDate", string.Empty);
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        if (lastScanDate == today)
        {
            TodayScanCount = Preferences.Get("Scanner.TodayScanCount", 0);
        }
        else
        {
            TodayScanCount = 0;
            Preferences.Set("Scanner.TodayScanCount", 0);
            Preferences.Set("Scanner.LastScanDate", today);
        }
    }

    private async Task UpdateStatisticsAsync(ScanStatus status)
    {
        if (status == ScanStatus.Accepted || status == ScanStatus.Queued)
        {
            TodayScanCount++;
            Preferences.Set("Scanner.TodayScanCount", TodayScanCount);
            Preferences.Set("Scanner.LastScanDate", DateTime.Today.ToString("yyyy-MM-dd"));
        }

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

    // ── Connectivity + sync event handlers ──────────────────────────────────

    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isOnline)
            {
                ConnectivityStatus = "Online";
                ConnectivityIcon = "🟢";
                ConnectivityColor = Color.FromArgb("#4CAF50");
            }
            else
            {
                ConnectivityStatus = "Offline";
                ConnectivityIcon = "🔴";
                ConnectivityColor = Color.FromArgb("#F44336");
            }
        });
    }

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                QueuePendingCount = await _offlineQueue.GetQueueCountAsync();
                HasQueuedScans = QueuePendingCount > 0;

                if (e.SyncedCount > 0 || e.FailedCount > 0 || e.SkippedCount > 0)
                {
                    var message = $"✓ Synced: {e.SyncedCount}";
                    if (e.FailedCount > 0) message += $" | ✗ Failed: {e.FailedCount}";
                    if (e.SkippedCount > 0) message += $" | ⏭ Skipped: {e.SkippedCount}";
                    if (!string.IsNullOrEmpty(e.FirstErrorMessage))
                        message += $"\nError: {e.FirstErrorMessage}";

                    LastScanMessage = message;
                    FeedbackColor = e.SyncedCount > 0 ? Color.FromArgb("#4CAF50") : Color.FromArgb("#FF9800");
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

    // ── Scan logging ─────────────────────────────────────────────────────────

    private async Task LogScanToHistoryAsync(ScanResult result)
    {
        try
        {
            var processingTimeMs = result.ScannedAt != default
                ? (long)(DateTimeOffset.UtcNow - result.ScannedAt).TotalMilliseconds
                : 0L;

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
                ErrorDetails = BuildTechnicalDetail(result),
                ScanMethod = _scannerMode,
                CameraIndex = result.CameraIndex,
                CameraName = result.CameraName
            };

            await _scanHistory.LogScanAsync(logEntry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log scan to history");
        }
    }

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

    private static string ToFriendlyMessage(ScanResult result)
    {
        return result.Status switch
        {
            ScanStatus.Accepted when result.IsVisitorScan =>
                $"✓ Visitor Pass #{result.PassNumber} — {result.ScanType}",
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
        var serverMessage = result.Message;
        if (!string.IsNullOrEmpty(serverMessage) &&
            !serverMessage.Equals("QR code rejected by server.", StringComparison.OrdinalIgnoreCase))
        {
            return $"✗ {serverMessage}";
        }

        var reason = result.ValidationResult?.RejectionReason ?? string.Empty;

        if (reason.StartsWith("Expired", StringComparison.Ordinal))
            return "✗ QR code expired — student needs a new ID card";

        if (reason.StartsWith("SecretUnavailable", StringComparison.Ordinal))
            return "✗ Device not configured — contact IT support";

        return "✗ Invalid QR code";
    }

    private static string ToFriendlyErrorMessage(ScanResult result)
    {
        var errorReason = result.ErrorReason ?? string.Empty;

        if (errorReason is "MissingApiKey" or "MissingServerUrl")
            return $"✗ {result.Message}";

        if (errorReason == "InvalidApiKey")
            return "✗ Device not authorised — contact IT support";

        if (errorReason == "NetworkError")
            return "✗ No connection — check your network";

        if (errorReason == "Cancelled")
            return "✗ Scan cancelled";

        return "✗ Something went wrong — please try again";
    }
}
