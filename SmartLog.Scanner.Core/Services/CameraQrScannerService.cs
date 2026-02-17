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

            // US0017 AC1/AC2: Check online/offline status for routing decision
            if (_healthCheck.IsOnline == true)
            {
                // US0017 AC1: Online path - submit to server
                _logger.LogDebug("Server online - submitting scan to API");
                scanResult = await _scanApi.SubmitScanAsync(payload, scannedAt, scanType);

                // US0017 AC3: Mid-request failure fallback to queue
                if (scanResult.Status == ScanStatus.Queued || scanResult.Status == ScanStatus.Error)
                {
                    _logger.LogWarning("Server submission failed, falling back to offline queue");

                    try
                    {
                        // Check if already queued before enqueuing
                        var alreadyQueued = await _offlineQueue.HasPendingForStudentAsync(
                            validationResult.StudentId!, scanType);

                        if (!alreadyQueued)
                        {
                            await _offlineQueue.EnqueueScanAsync(payload, scannedAt, scanType);
                        }
                        else
                        {
                            _logger.LogDebug("Scan already queued for student {StudentId}, skipping duplicate enqueue",
                                validationResult.StudentId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to enqueue scan to offline queue");
                    }

                    scanResult = new ScanResult
                    {
                        RawPayload = payload,
                        ValidationResult = validationResult,
                        Status = ScanStatus.Queued,
                        Message = "Scan queued (offline)",
                        ScannedAt = scannedAt
                    };
                }
                else
                {
                    // Preserve validation result for compatibility
                    scanResult = scanResult with { ValidationResult = validationResult };
                }
            }
            else
            {
                // US0017 AC2: Offline path - queue directly without attempting server submission
                _logger.LogDebug("Server offline - queueing scan locally");

                try
                {
                    // Check if already queued for this student+scanType
                    var alreadyQueued = await _offlineQueue.HasPendingForStudentAsync(
                        validationResult.StudentId!, scanType);

                    if (alreadyQueued)
                    {
                        // Already in queue - show amber feedback instead of blue
                        _logger.LogDebug("Scan already queued for student {StudentId}, scan type {ScanType}",
                            validationResult.StudentId, scanType);

                        scanResult = new ScanResult
                        {
                            RawPayload = payload,
                            ValidationResult = validationResult,
                            Status = ScanStatus.DebouncedLocally,
                            Message = "Already queued. Please proceed.",
                            StudentId = validationResult.StudentId,
                            ScannedAt = scannedAt
                        };
                    }
                    else
                    {
                        // Not queued yet - enqueue it
                        await _offlineQueue.EnqueueScanAsync(payload, scannedAt, scanType);

                        scanResult = new ScanResult
                        {
                            RawPayload = payload,
                            ValidationResult = validationResult,
                            Status = ScanStatus.Queued,
                            Message = "Scan queued (offline)",
                            ScannedAt = scannedAt
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue scan to offline queue");

                    scanResult = new ScanResult
                    {
                        RawPayload = payload,
                        ValidationResult = validationResult,
                        Status = ScanStatus.Error,
                        Message = "Failed to save scan",
                        ScannedAt = scannedAt
                    };
                }
            }
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

        // Raise event for UI
        ScanCompleted?.Invoke(this, scanResult);
    }
}
