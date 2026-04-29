using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.Core.ViewModels;

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
    private readonly IHeartbeatService _heartbeat;
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

    /// <summary>Pill background color — blue for ENTRY, deep orange for EXIT.</summary>
    public Color ScanTypePillColor => CurrentScanType == "EXIT"
        ? Color.FromArgb("#E64A19")
        : Color.FromArgb("#1976D2");

    partial void OnCurrentScanTypeChanged(string value)
        => OnPropertyChanged(nameof(ScanTypePillColor));

    // EP0011: Camera that produced the most recent scan (used by logs / heartbeat — not UI-bound after US0124)
    [ObservableProperty] private string? _lastScanCameraName;

    // US0013: Statistics counters
    [ObservableProperty] private int _queuePendingCount;
    [ObservableProperty] private int _todayScanCount;

    // Offline queue visibility
    [ObservableProperty] private bool _hasQueuedScans;

    // US0015: Connectivity status indicator
    [ObservableProperty] private string _connectivityStatus = "Connecting...";
    [ObservableProperty] private string _connectivityIcon = "⚪";
    [ObservableProperty] private Color _connectivityColor = Color.FromArgb("#9E9E9E"); // Gray
    [ObservableProperty] private bool _isCheckingConnectivity;

    // Live clock display (US0092: split into time + date for two-line header)
    [ObservableProperty] private string _currentTime = string.Empty;
    [ObservableProperty] private string _currentDate = string.Empty;
    private IDispatcherTimer? _clockTimer;

    // Selected camera device ID (single-camera legacy; multi-camera uses CameraSlots)
    [ObservableProperty] private string _selectedCameraId = string.Empty;

    // EP0012/US0121: Mode helpers — supports "Camera", "USB", and concurrent "Both".
    public bool IsCameraMode => _scannerMode is "Camera" or "Both";
    public bool IsUsbMode => _scannerMode is "USB" or "Both";
    // EP0012/US0123: Left column shown whenever any scan pipeline is active.
    public bool ShowLeftColumn => IsCameraMode || IsUsbMode;

    // US0124: Body width fed by MainPage code-behind via SizeChanged. Drives CardWidth.
    [ObservableProperty] private double _bodyWidth;

    /// <summary>
    /// US0124: Per-card width — divides the body width equally among active cards (visible cameras + USB).
    /// 1 card = 100%; 2 cards = 50/50; 3 = 33×3; 4 = 25×4. Clamped to [260, 520] px so cards stay
    /// readable on narrow displays and don't blow up on wide ones (US0126 1080p ceiling).
    /// </summary>
    public double CardWidth
    {
        get
        {
            var visibleCameras = CameraSlots.Count(s => s.IsVisible);
            var totalActive = visibleCameras + (IsUsbMode ? 1 : 0);
            if (totalActive == 0 || BodyWidth <= 0) return 320;
            const double interCardSpacing = 12; // matches Margin="6" on each card side
            var available = BodyWidth - (interCardSpacing * (totalActive + 1));
            var perCard = available / totalActive;
            return Math.Clamp(perCard, 260, 520);
        }
    }

    partial void OnBodyWidthChanged(double value) => OnPropertyChanged(nameof(CardWidth));

    // US0126: Cards-area height fed from the ScrollView SizeChanged (lives in the * row,
    // so it already excludes the camera preview). Drives CardHeight.
    [ObservableProperty] private double _bodyHeight;

    /// <summary>
    /// US0126: Per-card height — fills the available vertical space in the cards area.
    /// Cards have Margin="6" (6px top + 6px bottom = 12px consumed by spacing).
    /// Clamped to a minimum so cards stay readable on very short displays.
    /// </summary>
    public double CardHeight
    {
        get
        {
            if (BodyHeight <= 0) return 390;
            return Math.Max(BodyHeight - 12, 300);
        }
    }

    partial void OnBodyHeightChanged(double value) => OnPropertyChanged(nameof(CardHeight));

    // US0124: Inline sync / queue status message shown in the statistics footer (replaces deleted bottom feedback banner).
    [ObservableProperty] private string? _syncStatusMessage;

    public bool HasSyncStatusMessage => !string.IsNullOrEmpty(SyncStatusMessage);

    partial void OnSyncStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasSyncStatusMessage));

    private CancellationTokenSource? _syncStatusCts;

    // EP0011: Fixed 8-slot observable collection. Slots beyond configured count have IsVisible=false.
    // Initialized in constructor (after DI) so RestartCommand callbacks can reference _multiCameraManager.
    public ObservableCollection<CameraSlotState> CameraSlots { get; }

    // EP0012/US0123: USB scanner indicator card state (visible only when IsUsbMode).
    public UsbScannerSlotState UsbScannerSlot { get; } = new();

    // EP0011: Per-slot flash animation cancellation tokens (prevent timer leaks on rapid scans)
    private readonly Dictionary<int, CancellationTokenSource> _flashTimers = new();

    // Per-camera scan gate: true while a slot is showing a result. Scans arriving while gated are
    // dropped entirely so the operator always sees one clean result before the next scan is accepted.
    private readonly bool[] _cameraGated = new bool[8];

    // EP0012/US0123: Single CTS for the USB card flash (parallel to _flashTimers for cameras)
    private CancellationTokenSource? _usbFlashCts;


    // EP0011: 1-second timer for per-slot frame rate display
    private IDispatcherTimer? _frameRateTimer;

    // Tracks the camera count active in the current pipeline; used to detect config changes after Setup.
    private int _loadedCameraCount;

    public MainViewModel(
        IMultiCameraManager multiCameraManager,
        UsbQrScannerService usbScanner,
        IPreferencesService preferences,
        ISoundService soundService,
        IOfflineQueueService offlineQueue,
        IHealthCheckService healthCheck,
        IHeartbeatService heartbeat,
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
        _heartbeat = heartbeat;
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

        // EP0012/US0121: Subscribe independently per active pipeline (supports "Both" mode).
        if (IsCameraMode)
        {
            _multiCameraManager.ScanCompleted += OnMultiCameraScanCompleted;
            _multiCameraManager.ScanUpdated += OnMultiCameraScanUpdated;
            _multiCameraManager.CameraStatusChanged += OnMultiCameraStatusChanged;
            StatusIcon = "📷";
        }

        if (IsUsbMode)
        {
            _usbScanner.ScanCompleted += OnScanCompleted;
            if (!IsCameraMode)
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

        // US0120: Start heartbeat service — pushes scanner vitals to admin server
        await _heartbeat.StartAsync();

        // Initialize connectivity pill from current health check state
        ApplyConnectivityState(_healthCheck.IsOnline);

        // US0124: Subscribe to slot IsVisible flips so CardWidth recomputes when a slot's visibility
        // toggles post-init (delayed enumeration / hot-add / recovery flow). Subscribe once for all
        // 8 slots — handler filters by property name. Unsubscribed in DisposeAsync.
        foreach (var slot in CameraSlots)
            slot.PropertyChanged += OnCameraSlotPropertyChanged;

        // EP0012/US0121: Start each pipeline independently — both run in "Both" mode.
        if (IsCameraMode)
        {
            var cameraCount = ResolveCameraCount();
            _loadedCameraCount = cameraCount;
            var cameraConfigs = BuildCameraConfigs(cameraCount);
            ApplyCameraConfigsToSlots(cameraConfigs, cameraCount);

            await _multiCameraManager.InitializeAsync(cameraConfigs);
            await _multiCameraManager.StartAllAsync();

            StatusMessage = "Ready to scan QR codes";
            StatusIcon = "📷";
        }

        if (IsUsbMode)
        {
            await _usbScanner!.StartAsync();

            // EP0012/US0123: activate indicator card and start the health heuristic
            UsbScannerSlot.IsVisible = true;
            UsbScannerSlot.ScanType = CurrentScanType;
            UsbScannerSlot.StartListening();

            if (!IsCameraMode)
            {
                StatusMessage = "Ready for USB scanner input";
                StatusIcon = "⌨️";
            }
        }

        // EP0012/US0121: 1-second frame-rate timer — needed whenever any pipeline is active.
        if (IsCameraMode || IsUsbMode)
        {
            _frameRateTimer = Application.Current!.Dispatcher.CreateTimer();
            _frameRateTimer.Interval = TimeSpan.FromSeconds(1);
            _frameRateTimer.Tick += OnFrameRateTick;
            _frameRateTimer.Start();
        }

        IsScanning = true;
    }

    // Reads the persisted count, then walks saved device IDs to catch cameras configured
    // before SetCameraCount() was wired up (pre-fix installs).
    private int ResolveCameraCount()
    {
        var count = _preferences.GetCameraCount();
        for (var i = count; i < 8; i++)
        {
            if (!string.IsNullOrEmpty(_preferences.GetCameraDeviceId(i)))
                count = i + 1;
            else
                break;
        }
        return Math.Clamp(count, 1, 8);
    }

    // Called by MainPage.OnAppearing when returning from Setup. Rebuilds the camera
    // pipeline if the saved config (count, device IDs, or enabled states) differs from
    // what's currently running. Returns true if the pipeline was restarted.
    public async Task<bool> ReloadCameraConfigAsync()
    {
        if (!IsCameraMode) return false;
        var newCount = ResolveCameraCount();
        var newConfigs = BuildCameraConfigs(newCount);

        if (!CameraConfigChanged(newCount, newConfigs)) return false;

        await _multiCameraManager.StopAllAsync();
        ApplyCameraConfigsToSlots(newConfigs, newCount);
        _loadedCameraCount = newCount;
        await _multiCameraManager.InitializeAsync(newConfigs);
        await _multiCameraManager.StartAllAsync();
        return true;
    }

    // Returns true if the new config differs from what's loaded in the manager.
    // Compares count, per-camera device IDs, and enabled states so a slot that
    // was disabled and then re-enabled triggers a reload even when count is unchanged.
    private bool CameraConfigChanged(int newCount, List<CameraInstance> newConfigs)
    {
        var current = _multiCameraManager.Cameras;
        if (current.Count != newCount) return true;
        for (var i = 0; i < newCount; i++)
        {
            if (current[i].CameraDeviceId != newConfigs[i].CameraDeviceId) return true;
            if (current[i].IsEnabled != newConfigs[i].IsEnabled) return true;
        }
        return false;
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
        // Drop the scan while this camera's slot is still showing a result.
        // The gate is cleared when the 1s flash timer fires, ensuring one clean result per cycle.
        if (e.CameraIndex >= 0 && e.CameraIndex < _cameraGated.Length && _cameraGated[e.CameraIndex])
            return;

        MainThread.BeginInvokeOnMainThread(() => TriggerSlotFlash(e.CameraIndex, e.Result));

        OnScanCompleted(sender, e.Result);
    }

    /// <summary>
    /// Routes a multi-camera ScanUpdated event: server-confirmed result re-paints the source slot
    /// (cancels the optimistic flash CTS and starts a fresh 1s window with the corrected data),
    /// then delegates persistence/audio/stats to OnScanUpdated.
    /// </summary>
    private void OnMultiCameraScanUpdated(object? sender, (int CameraIndex, ScanResult Result) e)
    {
        MainThread.BeginInvokeOnMainThread(() => TriggerSlotFlash(e.CameraIndex, e.Result));

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

    /// <summary>
    /// US0124: Recompute CardWidth when any slot's IsVisible flips (delayed enumeration,
    /// hot-add, recovery). Subscribed in InitializeAsync, unsubscribed in DisposeAsync.
    /// </summary>
    private void OnCameraSlotPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraSlotState.IsVisible))
            OnPropertyChanged(nameof(CardWidth));
    }

    // ── EP0011: Per-slot flash animation ────────────────────────────────────

    /// <summary>
    /// US0124: Shows a 1-second status flash on the specified camera station card with full
    /// scan details (student number, name, LRN, grade · program · section, time, coloured banner).
    /// A new scan on the same slot cancels the previous timer so the card never resets mid-flash.
    /// While the flash is showing, _cameraGated[cameraIndex] is true and incoming scans are dropped
    /// (see OnMultiCameraScanCompleted gate check).
    /// </summary>
    private void TriggerSlotFlash(int cameraIndex, ScanResult result)
    {
        if (cameraIndex < 0 || cameraIndex >= CameraSlots.Count) return;

        if (_flashTimers.TryGetValue(cameraIndex, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        var cts = new CancellationTokenSource();
        _flashTimers[cameraIndex] = cts;

        if (cameraIndex < _cameraGated.Length)
            _cameraGated[cameraIndex] = true;

        var slot = CameraSlots[cameraIndex];
        // Q2 fallback: prefer name, then student ID, then empty (visitor scans use the pass label).
        var subjectName = result.IsVisitorScan
            ? $"Visitor Pass #{result.PassNumber}"
            : result.StudentName ?? result.StudentId ?? string.Empty;

        slot.LastScanStatus = result.Status;
        slot.LastScanMessage = ToFriendlyMessage(result);
        slot.FlashStudentName = subjectName;
        slot.LastStudentId = result.IsVisitorScan ? null : result.StudentId;
        slot.LastLrn = result.IsVisitorScan ? null : result.Lrn;
        slot.LastGrade = result.IsVisitorScan ? null : result.Grade;
        slot.LastSection = result.IsVisitorScan ? null : result.Section;
        slot.LastProgram = result.IsVisitorScan ? null : result.Program;
        slot.LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
        slot.IsVisitorScan = result.IsVisitorScan;
        slot.ShowFlash = true;

        _ = Task.Delay(1000, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;

            if (cameraIndex < _cameraGated.Length)
                _cameraGated[cameraIndex] = false;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                slot.ShowFlash = false;
                slot.FlashStudentName = null;
                slot.LastScanMessage = null;
                slot.LastScanStatus = null;
                slot.LastStudentId = null;
                slot.LastLrn = null;
                slot.LastGrade = null;
                slot.LastSection = null;
                slot.LastProgram = null;
                slot.LastScanTime = null;
                slot.IsVisitorScan = false;
            });
        });
    }

    // ── EP0012/US0123: USB slot flash ────────────────────────────────────────

    private void TriggerUsbSlotFlash(ScanResult result)
    {
        _usbFlashCts?.Cancel();
        _usbFlashCts?.Dispose();
        _usbFlashCts = new CancellationTokenSource();
        var cts = _usbFlashCts;

        var subjectName = result.IsVisitorScan
            ? $"Visitor Pass #{result.PassNumber}"
            : result.StudentName ?? result.StudentId ?? string.Empty;

        UsbScannerSlot.LastScanStatus = result.Status;
        UsbScannerSlot.LastScanMessage = ToFriendlyMessage(result);
        UsbScannerSlot.FlashStudentName = subjectName;
        UsbScannerSlot.LastStudentId = result.IsVisitorScan ? null : result.StudentId;
        UsbScannerSlot.LastLrn = result.IsVisitorScan ? null : result.Lrn;
        UsbScannerSlot.LastGrade = result.IsVisitorScan ? null : result.Grade;
        UsbScannerSlot.LastSection = result.IsVisitorScan ? null : result.Section;
        UsbScannerSlot.LastProgram = result.IsVisitorScan ? null : result.Program;
        UsbScannerSlot.LastScanTime = result.ScannedAt.ToLocalTime().ToString("HH:mm:ss");
        UsbScannerSlot.IsVisitorScan = result.IsVisitorScan;
        UsbScannerSlot.ShowFlash = true;
        UsbScannerSlot.LastScanAt = result.ScannedAt;
        UsbScannerSlot.IsHealthWarning = false;

        _ = Task.Delay(1000, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UsbScannerSlot.ShowFlash = false;
                UsbScannerSlot.FlashStudentName = null;
                UsbScannerSlot.LastScanMessage = null;
                UsbScannerSlot.LastScanStatus = null;
                UsbScannerSlot.LastStudentId = null;
                UsbScannerSlot.LastLrn = null;
                UsbScannerSlot.LastGrade = null;
                UsbScannerSlot.LastSection = null;
                UsbScannerSlot.LastProgram = null;
                UsbScannerSlot.LastScanTime = null;
                UsbScannerSlot.IsVisitorScan = false;
            });
        });
    }

    // ── EP0011: Frame rate timer ─────────────────────────────────────────────

    private void OnFrameRateTick(object? sender, EventArgs e)
    {
        foreach (var slot in CameraSlots)
            slot.UpdateFrameRate();

        if (IsUsbMode)
            UsbScannerSlot.Tick();
    }

    // ── EP0011: Public methods for MainPage code-behind ──────────────────────

    /// <summary>
    /// Routes a barcode detected by a specific CameraQrView to the multi-camera manager.
    /// Called from MainPage.xaml.cs BarcodeDetected event handlers.
    /// </summary>
    public Task OnBarcodeFromCameraAsync(int cameraIndex, string payload)
    {
        if (IsCameraMode && !string.IsNullOrEmpty(payload))
            return _multiCameraManager.ProcessQrCodeAsync(cameraIndex, payload);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops all active scan pipelines — called from Window.Destroying.
    /// </summary>
    public async Task StopCamerasAsync()
    {
        if (IsCameraMode)
            await _multiCameraManager.StopAllAsync();
        if (IsUsbMode)
            await _usbScanner!.StopAsync();
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

        // EP0012/US0123: route USB-sourced scans into the USB station card.
        // Camera scans are flashed by OnMultiCameraScanCompleted (separate event handler).
        if (result.Source == ScanSource.UsbScanner && IsUsbMode)
            MainThread.BeginInvokeOnMainThread(() => TriggerUsbSlotFlash(result));

        // Audio + statistics. Audio for optimistic Accepted is deferred to OnScanUpdated so
        // a server-side downgrade (Duplicate / Rejected) doesn't fire a false success beep first.
        if (!result.IsOptimistic)
        {
            _ = _soundService.PlayResultSoundAsync(result.Status);
            _ = UpdateStatisticsAsync(result.Status);
        }

        LastScanCameraName = result.CameraName;
    }

    /// <summary>
    /// Handles server confirmation or correction of an optimistic camera scan result.
    /// The per-slot card is re-painted by OnMultiCameraScanUpdated → TriggerSlotFlash;
    /// this method only persists, updates statistics, and corrects the result sound if needed.
    /// </summary>
    private void OnScanUpdated(object? sender, ScanResult result)
    {
        _ = LogScanToHistoryAsync(result);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = UpdateStatisticsAsync(result.Status);

            // Optimistic Accepted → confirmed result. Play the actual outcome sound.
            // (For non-optimistic flows we already played sound in OnScanCompleted.)
            _ = _soundService.PlayResultSoundAsync(result.Status);
        });
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>
    /// US0124: Sets the inline status message in the statistics footer with auto-clear after the
    /// given timeout. Replaces the six fire-and-forget Task.Delay chains tied to the deleted
    /// shared bottom feedback banner. New calls cancel the previous timer so messages don't stack.
    /// </summary>
    private void SetSyncStatus(string message, int autoclearMs = 3000)
    {
        SyncStatusMessage = message;
        _syncStatusCts?.Cancel();
        _syncStatusCts?.Dispose();
        var cts = _syncStatusCts = new CancellationTokenSource();
        _ = Task.Delay(autoclearMs, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() => SyncStatusMessage = null);
        });
    }

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
            SetSyncStatus("ℹ️ Queue is already empty");
            return;
        }

        var confirmed = await Application.Current!.MainPage!.DisplayAlert(
            "Clear Queue",
            $"Delete {count} pending scan{(count == 1 ? "" : "s")} from queue?\n\nThis cannot be undone.",
            "Clear",
            "Cancel");

        if (!confirmed) return;

        SetSyncStatus("⏳ Clearing queue...", autoclearMs: 60000);

        try
        {
            await _offlineQueue.ClearPendingScansAsync();
            QueuePendingCount = 0;
            SetSyncStatus("✓ Queue cleared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear queue");
            SetSyncStatus($"✗ Failed to clear queue: {ex.Message}", autoclearMs: 5000);
        }
    }

    /// <summary>
    /// Manual sync command — triggers background sync immediately.
    /// </summary>
    [RelayCommand]
    private async Task ManualSync()
    {
        _logger.LogInformation("Manual sync triggered by user");
        SetSyncStatus("⏳ Syncing queued scans...", autoclearMs: 60000);

        try
        {
            await _backgroundSync.TriggerSyncAsync();
            // OnSyncCompleted will overwrite with the result message.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            SetSyncStatus($"✗ Sync failed: {ex.Message}", autoclearMs: 5000);
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
            SetSyncStatus("⚠️ HMAC secret not configured. Complete setup first.");
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

        if (IsCameraMode)
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

        if (IsCameraMode)
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

        // Cancel all in-progress flash timers and clear gates
        foreach (var cts in _flashTimers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _flashTimers.Clear();
        Array.Clear(_cameraGated, 0, _cameraGated.Length);

        _syncStatusCts?.Cancel();
        _syncStatusCts?.Dispose();
        _syncStatusCts = null;

        foreach (var slot in CameraSlots)
            slot.PropertyChanged -= OnCameraSlotPropertyChanged;

        // EP0012/US0121: Stop each active pipeline independently.
        if (IsCameraMode)
            await _multiCameraManager.StopAllAsync();
        if (IsUsbMode)
        {
            UsbScannerSlot.StopListening();
            _usbFlashCts?.Cancel();
            _usbFlashCts?.Dispose();
            _usbFlashCts = null;
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

        if (IsCameraMode)
        {
            _multiCameraManager.UpdateScanTypes(CurrentScanType);

            foreach (var cam in _multiCameraManager.Cameras)
            {
                if (cam.Index >= 0 && cam.Index < CameraSlots.Count)
                    CameraSlots[cam.Index].ScanType = cam.ScanType;
            }
        }

        if (IsUsbMode)
            UsbScannerSlot.ScanType = CurrentScanType;

        _logger.LogInformation("Scan type toggled to: {ScanType}", CurrentScanType);
    }

    // ── USB keyboard wedge methods ───────────────────────────────────────────

    /// <summary>
    /// US0008: Process keystroke from USB keyboard wedge scanner.
    /// </summary>
    public void ProcessKeystroke(string character)
    {
        if (IsUsbMode)
            _usbScanner?.ProcessKeystroke(character);
    }

    /// <summary>
    /// US0008: Process Enter key from USB keyboard wedge scanner.
    /// </summary>
    public void ProcessEnterKey()
    {
        if (IsUsbMode)
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

    /// <summary>Tap-to-refresh: triggers an immediate health check and shows a checking state.</summary>
    [RelayCommand]
    private async Task RefreshConnectivity()
    {
        if (IsCheckingConnectivity) return;

        IsCheckingConnectivity = true;
        ConnectivityStatus = "Checking...";
        ConnectivityIcon = "⚪";
        ConnectivityColor = Color.FromArgb("#9E9E9E");

        try
        {
            await _healthCheck.CheckNowAsync();
        }
        finally
        {
            IsCheckingConnectivity = false;
            // ConnectivityChanged only fires on state *change* — always sync pill here so
            // tapping while already online/offline still clears "Checking..." correctly.
            MainThread.BeginInvokeOnMainThread(() => ApplyConnectivityState(_healthCheck.IsOnline));
        }
    }

    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() => ApplyConnectivityState(isOnline));
    }

    private void ApplyConnectivityState(bool? isOnline)
    {
        if (isOnline == true)
        {
            ConnectivityStatus = "Online";
            ConnectivityIcon = "🟢";
            ConnectivityColor = Color.FromArgb("#4CAF50");
        }
        else if (isOnline == false)
        {
            ConnectivityStatus = "Offline";
            ConnectivityIcon = "🔴";
            ConnectivityColor = Color.FromArgb("#F44336");
        }
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

                    SetSyncStatus(message, autoclearMs: 8000);
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
                ScanMethod = result.Source.ToScanMethodString(),
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
