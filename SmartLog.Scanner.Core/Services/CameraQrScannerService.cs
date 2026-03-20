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
    /// Call this from ZXing BarcodesDetected event handler.
    /// </summary>
    public async Task ProcessQrCodeAsync(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;

        var now = DateTime.UtcNow;

        // AC3: 500ms raw payload debounce (performance optimization only)
        if (payload == _lastPayload && (now - _lastScanTime) < _debounceWindow)
        {
            _logger.LogDebug("Duplicate QR payload within {Ms}ms debounce window, ignoring",
                _debounceWindow.TotalMilliseconds);
            return;
        }

        // Post-scan lockout: same payload within warn window is silently ignored
        // Prevents re-processing while the QR code is still visible to the camera
        if (payload == _lastProcessedPayload && (now - _lastProcessedTime) < DeduplicationConfig.WarnWindow)
        {
            _logger.LogDebug("Same QR code still in view, ignoring (lockout {Sec}s remaining)",
                (DeduplicationConfig.WarnWindow - (now - _lastProcessedTime)).TotalSeconds);
            return;
        }

        // Update debounce tracking
        _lastPayload = payload;
        _lastScanTime = now;

        // AC4: Forward to HMAC validation
        _logger.LogInformation("Processing QR code: {Payload}", payload);
        var validationResult = await _hmacValidator.ValidateAsync(payload);

        ScanResult scanResult;

        if (validationResult.IsValid)
        {
            _logger.LogInformation("Valid QR code - StudentId: {StudentId}", validationResult.StudentId);

            var scanType = _preferences.GetDefaultScanType();
            var scannedAt = DateTimeOffset.UtcNow;

            // Student-level deduplication check (after HMAC validation)
            var dedupResult = _dedup.CheckAndRecord(
                validationResult.StudentId!,
                scanType,
                studentName: null); // Name will be populated by server response

            switch (dedupResult.Action)
            {
                case DeduplicationAction.SuppressSilent:
                    // Within 2s suppress window - no UI feedback, no event raised
                    _logger.LogDebug("Scan suppressed silently (within {Ms}ms of last scan)",
                        dedupResult.TimeSinceLastScan.TotalMilliseconds);
                    return;

                case DeduplicationAction.RejectWithFeedback:
                    // Within 30s warn window - show amber feedback, no API call
                    _logger.LogDebug("Scan rejected with feedback (within {Sec}s of last scan)",
                        dedupResult.TimeSinceLastScan.TotalSeconds);

                    scanResult = new ScanResult
                    {
                        RawPayload = payload,
                        ValidationResult = validationResult,
                        Status = ScanStatus.DebouncedLocally,
                        Message = dedupResult.Message ?? "Already scanned. Please proceed.",
                        StudentId = validationResult.StudentId,
                        ScannedAt = scannedAt
                    };

                    ScanCompleted?.Invoke(this, scanResult);
                    return;

                case DeduplicationAction.Proceed:
                    // Beyond warn window or first scan - continue to online/offline routing
                    _logger.LogDebug("Deduplication check passed, proceeding to submission");
                    break;
            }

            // ALWAYS ONLINE MODE: Offline queue disabled, always submit to server
            _logger.LogInformation("Always-online mode: submitting scan to server");
            System.Diagnostics.Debug.WriteLine("[CameraScanner] Always-online mode enabled");

            // Submit to server regardless of health check status
            scanResult = await _scanApi.SubmitScanAsync(payload, scannedAt, scanType);

            // Preserve validation result for compatibility
            scanResult = scanResult with { ValidationResult = validationResult };
        }
        else
        {
            _logger.LogWarning("Invalid QR code - Reason: {Reason}", validationResult.RejectionReason);

            // Invalid HMAC - don't submit to server
            scanResult = new ScanResult
            {
                RawPayload = payload,
                ValidationResult = validationResult,
                Status = ScanStatus.Rejected,
                Message = validationResult.RejectionReason,
                ScannedAt = DateTimeOffset.UtcNow
            };
        }

        // Lock out this payload so the camera doesn't re-process while QR is still visible
        _lastProcessedPayload = payload;
        _lastProcessedTime = DateTime.UtcNow;

        // Raise event for UI
        ScanCompleted?.Invoke(this, scanResult);
    }
}
