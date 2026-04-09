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
    private readonly ILogger<CameraQrScannerService> _logger;

    private string? _lastPayload;
    private DateTime _lastScanTime = DateTime.MinValue;
    private readonly TimeSpan _debounceWindow = DeduplicationConfig.CameraRawDebounce;
    private DateTime _lastProcessedTime = DateTime.MinValue;
    private string? _lastProcessedPayload;

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
        ILogger<CameraQrScannerService> logger)
    {
        _hmacValidator = hmacValidator;
        _scanApi = scanApi;
        _healthCheck = healthCheck;
        _offlineQueue = offlineQueue;
        _preferences = preferences;
        _dedup = dedup;
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

        // Post-scan lockout: same payload within warn window is silently ignored
        if (payload == _lastProcessedPayload && (now - _lastProcessedTime) < DeduplicationConfig.WarnWindow)
        {
            _logger.LogDebug("Same QR code still in view, ignoring (lockout {Sec}s remaining)",
                (DeduplicationConfig.WarnWindow - (now - _lastProcessedTime)).TotalSeconds);
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

        var scanType = _preferences.GetDefaultScanType();
        var scannedAt = DateTimeOffset.UtcNow;

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
        // or correct the result if the server rejects (e.g. inactive student, not a school day)
        _ = Task.Run(async () =>
        {
            try
            {
                var serverResult = await _scanApi.SubmitScanAsync(payload, scannedAt, scanType);
                ScanUpdated?.Invoke(this, serverResult with { ValidationResult = validationResult, ScannedAt = scannedAt });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting scan to server after optimistic acceptance");
            }
        });
    }
}
