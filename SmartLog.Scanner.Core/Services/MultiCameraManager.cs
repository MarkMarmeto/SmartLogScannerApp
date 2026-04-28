using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// EP0011 (US0066–US0070): Orchestrates 1–8 simultaneous camera QR scanner instances.
///
/// Design notes:
/// - Each camera gets its own CameraQrScannerService instance (independent debounce/lockout state).
/// - All instances share the same IScanDeduplicationService singleton (cross-camera dedup automatic).
/// - Instances are created via IServiceProvider so all DI-resolved singletons are shared correctly.
/// - Error isolation: exceptions in one camera do not affect others.
/// - Auto-recovery: up to 3 attempts per camera, 10s apart. Uses per-camera CancellationTokenSource
///   to prevent duplicate recovery loops (race condition guard).
/// </summary>
public class MultiCameraManager : IMultiCameraManager, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICameraWorkerFactory _workerFactory;
    private readonly IPreferencesService _preferences;
    private readonly ILogger<MultiCameraManager> _logger;

    private readonly List<CameraInstance> _cameras = new();
    private readonly Dictionary<int, CameraQrScannerService> _services = new();
    private readonly Dictionary<int, ICameraWorker> _workers = new();
    private readonly Dictionary<int, CancellationTokenSource> _recoveryCts = new();

    // Watchdog
    private CancellationTokenSource? _watchdogCts;
    private Task? _watchdogTask;

    public IReadOnlyList<CameraInstance> Cameras => _cameras.AsReadOnly();

    public event EventHandler<(int CameraIndex, ScanResult Result)>? ScanCompleted;
    public event EventHandler<(int CameraIndex, ScanResult Result)>? ScanUpdated;
    public event EventHandler<(int CameraIndex, CameraStatus Status)>? CameraStatusChanged;

    public MultiCameraManager(
        IServiceProvider serviceProvider,
        ICameraWorkerFactory workerFactory,
        IPreferencesService preferences,
        ILogger<MultiCameraManager> logger)
    {
        _serviceProvider = serviceProvider;
        _workerFactory = workerFactory;
        _preferences = preferences;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task InitializeAsync(IReadOnlyList<CameraInstance> cameras)
    {
        if (cameras.Count > 8)
            throw new ArgumentException("Maximum 8 cameras are supported.", nameof(cameras));

        _cameras.Clear();
        _services.Clear();

        foreach (var cam in cameras)
        {
            _cameras.Add(cam);

            // QR processing service — pure business logic, no hardware ownership
            var service = ActivatorUtilities.CreateInstance<CameraQrScannerService>(_serviceProvider);
            service.SetCameraIndex(cam.Index);
            service.SetCameraName(cam.DisplayName);
            service.SetScanTypeOverride(cam.ScanType);
            service.ScanCompleted += (_, result) => ScanCompleted?.Invoke(this, (cam.Index, result));
            service.ScanUpdated  += (_, result) => ScanUpdated?.Invoke(this, (cam.Index, result));
            _services[cam.Index] = service;

            // Headless camera worker — owns the hardware capture session, no native view
            var worker = _workerFactory.Create();
            worker.QrCodeDetected += async (_, payload) =>
            {
                cam.LastFrameAt = DateTime.UtcNow;
                await ProcessQrCodeAsync(cam.Index, payload);
            };
            worker.ErrorOccurred += (_, error) =>
                _ = HandleCameraErrorAsync(cam.Index, error);
            _workers[cam.Index] = worker;

            if (string.IsNullOrWhiteSpace(cam.CameraDeviceId))
            {
                cam.Status = CameraStatus.Offline;
                cam.ErrorMessage = "No device assigned";
                _logger.LogWarning("Camera {Index} has no device ID assigned — marked Offline", cam.Index);
            }
        }

        UpdateThrottleValues();
        _logger.LogInformation("MultiCameraManager initialized with {Count} camera(s)", cameras.Count);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StartAllAsync()
    {
        foreach (var cam in _cameras)
        {
            if (!cam.IsEnabled || cam.Status == CameraStatus.Offline)
                continue;

            await StartCameraInternalAsync(cam.Index);
        }

        StartWatchdog();
    }

    /// <inheritdoc/>
    public async Task StopAllAsync()
    {
        StopWatchdog();

        foreach (var cam in _cameras)
        {
            if (cam.Status == CameraStatus.Scanning)
                await StopCameraInternalAsync(cam.Index, manualStop: false);
        }
    }

    /// <inheritdoc/>
    public async Task StopCameraAsync(int cameraIndex)
    {
        // Manual stop: cancel any running recovery loop first
        CancelRecovery(cameraIndex);

        var cam = GetCamera(cameraIndex);
        if (cam == null) return;

        // Mark as manually disabled so auto-recovery does not restart it
        cam.IsEnabled = false;
        await StopCameraInternalAsync(cameraIndex, manualStop: true);
    }

    /// <inheritdoc/>
    public async Task RestartCameraAsync(int cameraIndex)
    {
        // Cancel any existing recovery loop before restarting
        CancelRecovery(cameraIndex);

        var cam = GetCamera(cameraIndex);
        if (cam == null) return;

        // Stop the worker/service first — StartCameraInternalAsync skips if worker.IsRunning is true.
        // A camera in Error state may still have IsRunning=true (capture session opened before error fired).
        await StopCameraInternalAsync(cameraIndex, manualStop: true);

        cam.IsEnabled = true;
        cam.ReconnectAttempts = 0;
        cam.ErrorMessage = null;
        await StartCameraInternalAsync(cameraIndex);
    }

    /// <inheritdoc/>
    public async Task ProcessQrCodeAsync(int cameraIndex, string payload)
    {
        if (!_services.TryGetValue(cameraIndex, out var service))
        {
            _logger.LogWarning("ProcessQrCodeAsync called for unknown camera index {Index}", cameraIndex);
            return;
        }

        // Update LastFrameAt so the watchdog doesn't fire while the camera is actively detecting QR codes.
        var cam = GetCamera(cameraIndex);
        if (cam != null)
            cam.LastFrameAt = DateTime.UtcNow;

        try
        {
            await service.ProcessQrCodeAsync(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Camera {Index} threw an exception during QR processing", cameraIndex);
            await HandleCameraErrorAsync(cameraIndex, ex.Message);
        }
    }

    /// <inheritdoc/>
    public void UpdateThrottleValues()
    {
        var activeCount = _cameras.Count(c => c.IsEnabled && c.Status != CameraStatus.Offline);
        var throttle = AdaptiveDecodeThrottle.Calculate(Math.Max(activeCount, 1));

        foreach (var cam in _cameras)
            cam.DecodeThrottleFrames = throttle;

        _logger.LogDebug("Throttle updated: {ActiveCount} active cameras → skip every {Throttle} frames",
            activeCount, throttle);
    }

    /// <inheritdoc/>
    public void UpdateScanTypes(string scanType)
    {
        foreach (var cam in _cameras)
        {
            cam.ScanType = scanType;

            if (_services.TryGetValue(cam.Index, out var service))
                service.SetScanTypeOverride(scanType);
        }

        _logger.LogDebug("Scan type updated for all cameras: {ScanType}", scanType);
    }

    /// <inheritdoc/>
    public void UpdateCameraName(int cameraIndex, string name)
    {
        if (_services.TryGetValue(cameraIndex, out var service))
            service.SetCameraName(name);

        var cam = GetCamera(cameraIndex);
        if (cam != null)
            cam.DisplayName = name;
    }

    /// <inheritdoc/>
    public void ConfigureCameraPreview(int cameraIndex, Action<ICameraWorker> configure)
    {
        if (_workers.TryGetValue(cameraIndex, out var worker))
            configure(worker);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task StartCameraInternalAsync(int cameraIndex)
    {
        var cam = GetCamera(cameraIndex);
        if (cam == null || !_services.TryGetValue(cameraIndex, out var service)) return;
        if (!_workers.TryGetValue(cameraIndex, out var worker)) return;

        if (worker.IsRunning)
        {
            _logger.LogDebug("Camera {Index} is already running — start skipped", cameraIndex);
            return;
        }

        try
        {
            await service.StartAsync();
            await worker.StartAsync(cam.CameraDeviceId);
            cam.Status = CameraStatus.Scanning;
            cam.ErrorMessage = null;
            cam.LastFrameAt = DateTime.UtcNow;
            RaiseCameraStatusChanged(cameraIndex, CameraStatus.Scanning);
            _logger.LogInformation("Camera {Index} started (device={DeviceId})", cameraIndex, cam.CameraDeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Camera {Index} failed to start", cameraIndex);
            await HandleCameraErrorAsync(cameraIndex, ex.Message);
        }
    }

    private async Task StopCameraInternalAsync(int cameraIndex, bool manualStop)
    {
        var cam = GetCamera(cameraIndex);
        if (cam == null) return;

        if (_services.TryGetValue(cameraIndex, out var service))
        {
            try { await service.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Camera {Index} service threw while stopping", cameraIndex); }
        }

        if (_workers.TryGetValue(cameraIndex, out var worker))
        {
            try { await worker.StopAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Camera {Index} worker threw while stopping", cameraIndex); }
        }

        cam.Status = manualStop ? CameraStatus.Idle : CameraStatus.Error;
        RaiseCameraStatusChanged(cameraIndex, cam.Status);
        _logger.LogInformation("Camera {Index} stopped (manual={Manual})", cameraIndex, manualStop);
    }

    private Task HandleCameraErrorAsync(int cameraIndex, string? errorMessage)
    {
        var cam = GetCamera(cameraIndex);
        if (cam == null) return Task.CompletedTask;

        cam.Status = CameraStatus.Error;
        cam.ErrorMessage = errorMessage;
        RaiseCameraStatusChanged(cameraIndex, CameraStatus.Error);
        _logger.LogWarning("Camera {Index} entered Error state: {Error}", cameraIndex, errorMessage);

        // Only auto-recover if camera was not manually stopped
        if (cam.IsEnabled)
            TriggerAutoRecovery(cameraIndex);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Fire-and-forget auto-recovery loop. At most one loop runs per camera (guarded by _recoveryCts).
    /// </summary>
    private void TriggerAutoRecovery(int cameraIndex)
    {
        const int MaxRetries = 3;
        const int RetryDelayMs = 10_000;

        // Cancel any existing recovery loop for this camera (race condition guard)
        CancelRecovery(cameraIndex);

        var cts = new CancellationTokenSource();
        _recoveryCts[cameraIndex] = cts;

        var cam = GetCamera(cameraIndex);
        if (cam == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                while (cam.ReconnectAttempts < MaxRetries && !cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(RetryDelayMs, cts.Token);

                    if (!cam.IsEnabled || cts.Token.IsCancellationRequested)
                        break;

                    cam.ReconnectAttempts++;
                    _logger.LogInformation("Camera {Index}: auto-recovery attempt {Attempt}/{Max}",
                        cameraIndex, cam.ReconnectAttempts, MaxRetries);

                    await StartCameraInternalAsync(cameraIndex);

                    if (cam.Status == CameraStatus.Scanning)
                    {
                        _logger.LogInformation("Camera {Index}: auto-recovery succeeded", cameraIndex);
                        cam.ReconnectAttempts = 0;
                        return;
                    }
                }

                if (!cts.Token.IsCancellationRequested && cam.Status != CameraStatus.Scanning)
                {
                    cam.Status = CameraStatus.Offline;
                    cam.ErrorMessage = "Device offline — all reconnect attempts failed";
                    RaiseCameraStatusChanged(cameraIndex, CameraStatus.Offline);
                    _logger.LogWarning("Camera {Index}: all {Max} recovery attempts failed → Offline",
                        cameraIndex, MaxRetries);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Camera {Index}: recovery loop cancelled cleanly", cameraIndex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Camera {Index}: unexpected error in recovery loop", cameraIndex);
            }
        }, cts.Token);
    }

    private void StartWatchdog()
    {
        _watchdogCts = new CancellationTokenSource();
        var token = _watchdogCts.Token;

        _watchdogTask = Task.Run(async () =>
        {
            const int WatchdogIntervalMs = 60_000;       // check every 60s
            const int NoSignalThresholdMs = 300_000;     // fire only if silent for 5 minutes

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(WatchdogIntervalMs, token);

                    var now = DateTime.UtcNow;
                    foreach (var cam in _cameras)
                    {
                        if (cam.Status != CameraStatus.Scanning) continue;
                        if (cam.LastFrameAt == null) continue;

                        var elapsed = (now - cam.LastFrameAt.Value).TotalMilliseconds;
                        if (elapsed > NoSignalThresholdMs)
                        {
                            _logger.LogWarning("Camera {Index}: no frames for {Elapsed:F0}ms — No Signal",
                                cam.Index, elapsed);
                            cam.ErrorMessage = "No Signal — camera not responding";
                            await HandleCameraErrorAsync(cam.Index, cam.ErrorMessage);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Watchdog loop error");
                }
            }
        }, token);
    }

    private void StopWatchdog()
    {
        _watchdogCts?.Cancel();
        _watchdogCts?.Dispose();
        _watchdogCts = null;
    }

    private void CancelRecovery(int cameraIndex)
    {
        if (_recoveryCts.TryGetValue(cameraIndex, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
            _recoveryCts.Remove(cameraIndex);
        }
    }

    private void RaiseCameraStatusChanged(int cameraIndex, CameraStatus status)
        => CameraStatusChanged?.Invoke(this, (cameraIndex, status));

    private CameraInstance? GetCamera(int cameraIndex)
    {
        var cam = _cameras.FirstOrDefault(c => c.Index == cameraIndex);
        if (cam == null)
            _logger.LogWarning("Camera index {Index} not found in manager", cameraIndex);
        return cam;
    }

    public async ValueTask DisposeAsync()
    {
        StopWatchdog();
        foreach (var cts in _recoveryCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _recoveryCts.Clear();

        if (_watchdogTask != null)
        {
            try { await _watchdogTask; }
            catch (OperationCanceledException) { }
        }

        foreach (var worker in _workers.Values)
        {
            try { await worker.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error disposing camera worker"); }
        }
        _workers.Clear();
    }
}
