using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Constants;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0007/US0017: Camera-based QR scanner service with payload debounce and student-level deduplication.
/// Processes QR codes from ZXing.Net.Maui camera events with seamless online/offline transitions.
/// </summary>
public class CameraQrScannerService : IQrScannerService
{
    private readonly IHmacValidator _hmacValidator;
    private readonly IScanApiService _scanApi;
    private readonly IHealthCheckService _healthCheck;
    private readonly IOfflineQueueService _offlineQueue;
    private readonly IPreferencesService _preferences;
    private readonly IScanDeduplicationService _dedup;
    private readonly ITimeService _timeService;
    private readonly ILogger<CameraQrScannerService> _logger;

    private string? _lastPayload;
    private DateTime _lastScanTime = DateTime.MinValue;
    private readonly TimeSpan _debounceWindow = DeduplicationConfig.CameraRawDebounce;
    private DateTime _lastProcessedTime = DateTime.MinValue;
    private string? _lastProcessedPayload;

    // EP0011: Camera index for multi-camera attribution. Null = single-camera mode (default).
    private int? _cameraIndex;

    /// <summary>
    /// Sets the camera index for scan attribution. Called by MultiCameraManager after construction.
    /// </summary>
    public void SetCameraIndex(int? cameraIndex) => _cameraIndex = cameraIndex;

    // EP0011: Per-camera scan type override. When set, used instead of _preferences.GetDefaultScanType().
    private string? _scanTypeOverride;

    /// <summary>
    /// Sets a scan type override for this camera instance. Called by MultiCameraManager.
    /// </summary>
    public void SetScanTypeOverride(string? scanType) => _scanTypeOverride = scanType;

    // How long to lock out the same QR payload after processing.
    // Matches the UI feedback duration (3s) so the scanner is ready the moment feedback clears.
    // The student-level dedup service handles repeat-student protection beyond this window.
    private static readonly TimeSpan PayloadLockoutWindow = TimeSpan.FromSeconds(3);

    public event EventHandler<ScanResult>? ScanCompleted;
    public event EventHandler<ScanResult>? ScanUpdated;
    public bool IsScanning { get; private set; }

    public CameraQrScannerService(
        IHmacValidator hmacValidator,
        IScanApiService scanApi,
        IHealthCheckService healthCheck,
        IOfflineQueueService offlineQueue,
        IPreferencesService preferences,
        IScanDeduplicationService dedup,
        ITimeService timeService,
        ILogger<CameraQrScannerService> logger)
    {
        _hmacValidator = hmacValidator;
        _scanApi = scanApi;
        _healthCheck = healthCheck;
        _offlineQueue = offlineQueue;
        _preferences = preferences;
        _dedup = dedup;
        _timeService = timeService;
        _logger = logger;
    }

    public Task StartAsync()
    {
        IsScanning = true;
        _logger.LogInformation("Camera QR scanner started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsScanning = false;
        _lastPayload = null;
        _logger.LogInformation("Camera QR scanner stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// AC3: Processes a decoded QR payload with raw payload debounce and student-level deduplication.
    /// Uses optimistic acceptance: fires ScanCompleted immediately after local validation,
    /// then submits to the server in background and fires ScanUpdated with the confirmed result.
    /// </summary>
    public async Task ProcessQrCodeAsync(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        var now = DateTime.UtcNow;

        // AC3: 500ms raw payload debounce (performance optimisation only)
        if (payload == _lastPayload && (now - _lastScanTime) < _debounceWindow)
        {
            _logger.LogDebug("Duplicate QR payload within {Ms}ms debounce window, ignoring",
                _debounceWindow.TotalMilliseconds);
            return;
        }

        // Post-scan lockout: same payload within lockout window is silently ignored.
        // 3s matches the UI feedback duration — once feedback clears, the same card can be re-scanned
        // and the student-level dedup service provides feedback for any repeats within its own window.
        if (payload == _lastProcessedPayload && (now - _lastProcessedTime) < PayloadLockoutWindow)
        {
            _logger.LogDebug("Same QR code still in view, ignoring (lockout {Sec}s remaining)",
                (PayloadLockoutWindow - (now - _lastProcessedTime)).TotalSeconds);
            return;
        }

        _lastPayload = payload;
        _lastScanTime = now;

        // AC4: HMAC validation (~10ms)
        _logger.LogInformation("Processing QR code: {Payload}", payload);
        var validationResult = await _hmacValidator.ValidateAsync(payload);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Invalid QR code - Reason: {Reason}", validationResult.RejectionReason);
            ScanCompleted?.Invoke(this, new ScanResult
            {
                RawPayload = payload,
                ValidationResult = validationResult,
                Status = ScanStatus.Rejected,
                Message = validationResult.RejectionReason,
                ScannedAt = DateTimeOffset.UtcNow
            });
            return;
        }

        _logger.LogInformation("Valid QR code - StudentId: {StudentId}", validationResult.StudentId);

        var scanType = _scanTypeOverride ?? _preferences.GetDefaultScanType();
        var scannedAt = _timeService.UtcNow;

        // Student-level deduplication check (~2ms)
        var dedupResult = _dedup.CheckAndRecord(validationResult.StudentId!, scanType, studentName: null);

        if (dedupResult.Action == DeduplicationAction.SuppressSilent)
        {
            _logger.LogDebug("Scan suppressed silently (within {Ms}ms of last scan)",
                dedupResult.TimeSinceLastScan.TotalMilliseconds);
            return;
        }

        if (dedupResult.Action == DeduplicationAction.RejectWithFeedback)
        {
            _logger.LogDebug("Scan rejected with feedback (within {Sec}s of last scan)",
                dedupResult.TimeSinceLastScan.TotalSeconds);
            ScanCompleted?.Invoke(this, new ScanResult
            {
                RawPayload = payload,
                ValidationResult = validationResult,
                Status = ScanStatus.DebouncedLocally,
                Message = dedupResult.Message ?? "Already scanned. Please proceed.",
                StudentId = validationResult.StudentId,
                ScannedAt = scannedAt
            });
            return;
        }

        // Lock out this payload immediately so the camera doesn't re-process while QR is still visible
        _lastProcessedPayload = payload;
        _lastProcessedTime = DateTime.UtcNow;

        // Optimistic acceptance: fire green feedback instantly based on local HMAC+dedup validation.
        // The server call happens in the background; ScanUpdated fires with the confirmed result.
        ScanCompleted?.Invoke(this, new ScanResult
        {
            RawPayload = payload,
            ValidationResult = validationResult,
            Status = ScanStatus.Accepted,
            StudentId = validationResult.StudentId,
            ScanType = scanType,
            ScannedAt = scannedAt,
            IsOptimistic = true
        });

        // Background: submit to server, then fire ScanUpdated so UI can update student info
        // or correct the result if the server rejects (e.g. inactive student, not a school day).
        // On network failure or rate limit, fall back to the offline queue so the scan is not lost.
        _ = Task.Run(async () =>
        {
            try
            {
                var serverResult = await _scanApi.SubmitScanAsync(payload, scannedAt, scanType, _cameraIndex);

                if (serverResult.Status == ScanStatus.Error || serverResult.Status == ScanStatus.RateLimited)
                {
                    _logger.LogWarning("Camera scan submission failed (Status={Status}), falling back to offline queue",
                        serverResult.Status);
                    await TryEnqueueAsync(payload, scannedAt, scanType, validationResult);
                    return;
                }

                ScanUpdated?.Invoke(this, serverResult with { ValidationResult = validationResult, ScannedAt = scannedAt });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Network error submitting camera scan — queuing offline");
                await TryEnqueueAsync(payload, scannedAt, scanType, validationResult);
            }
        });
    }

    /// <summary>
    /// Enqueues a camera scan that failed to reach the server.
    /// Deduplicates before inserting and fires ScanUpdated with Queued status so the UI reflects the fallback.
    /// </summary>
    private async Task TryEnqueueAsync(
        string payload,
        DateTimeOffset scannedAt,
        string scanType,
        HmacValidationResult validationResult)
    {
        try
        {
            var alreadyQueued = await _offlineQueue.HasPendingForStudentAsync(
                validationResult.StudentId!, scanType);

            if (!alreadyQueued)
            {
                await _offlineQueue.EnqueueScanAsync(payload, scannedAt, scanType);
                _logger.LogWarning("Camera scan queued offline: StudentId={StudentId}", validationResult.StudentId);
            }
            else
            {
                _logger.LogDebug("Camera scan already queued for {StudentId}, skipping duplicate", validationResult.StudentId);
            }

            ScanUpdated?.Invoke(this, new ScanResult
            {
                RawPayload = payload,
                ValidationResult = validationResult,
                Status = ScanStatus.Queued,
                Message = "Scan queued (offline)",
                StudentId = validationResult.StudentId,
                ScanType = scanType,
                ScannedAt = scannedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue camera scan to offline queue");
        }
    }
}
