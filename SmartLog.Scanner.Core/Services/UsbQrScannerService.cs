using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;
using System.Text;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0008/US0017: USB keyboard wedge QR scanner service with student-level deduplication.
/// Detects rapid keystrokes (< 100ms) from barcode scanners and validates QR codes with seamless online/offline transitions.
/// </summary>
public class UsbQrScannerService : IQrScannerService
{
    private readonly IHmacValidator _hmacValidator;
    private readonly IScanApiService _scanApi;
    private readonly IHealthCheckService _healthCheck;
    private readonly IOfflineQueueService _offlineQueue;
    private readonly IPreferencesService _preferences;
    private readonly IScanDeduplicationService _dedup;
    private readonly ITimeService _timeService;
    private readonly ILogger<UsbQrScannerService> _logger;

    private readonly StringBuilder _inputBuffer = new();
    private DateTime _lastKeystrokeTime = DateTime.MinValue;
    private readonly TimeSpan _interKeystrokeTimeout;
    private System.Threading.Timer? _timeoutTimer;
    private readonly object _bufferLock = new();

    public event EventHandler<ScanResult>? ScanCompleted;
    public event EventHandler<ScanResult>? ScanUpdated; // Not fired for USB; server response is awaited directly
    public bool IsScanning { get; private set; }

    public UsbQrScannerService(
        IHmacValidator hmacValidator,
        IScanApiService scanApi,
        IHealthCheckService healthCheck,
        IOfflineQueueService offlineQueue,
        IPreferencesService preferences,
        IScanDeduplicationService dedup,
        ITimeService timeService,
        ILogger<UsbQrScannerService> logger,
        TimeSpan? interKeystrokeTimeout = null)
    {
        _hmacValidator = hmacValidator;
        _scanApi = scanApi;
        _healthCheck = healthCheck;
        _offlineQueue = offlineQueue;
        _preferences = preferences;
        _dedup = dedup;
        _timeService = timeService;
        _logger = logger;
        _interKeystrokeTimeout = interKeystrokeTimeout ?? TimeSpan.FromMilliseconds(100);
    }

    public Task StartAsync()
    {
        IsScanning = true;
        _logger.LogInformation("USB QR scanner started - ready for keyboard wedge input");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsScanning = false;
        lock (_bufferLock)
        {
            _inputBuffer.Clear();
            _timeoutTimer?.Dispose();
            _timeoutTimer = null;
        }
        _logger.LogInformation("USB QR scanner stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// AC1/AC2: Processes a keystroke from keyboard wedge scanner.
    /// Call this from MainPage key event handler.
    /// </summary>
    public void ProcessKeystroke(string character)
    {
        if (!IsScanning || string.IsNullOrEmpty(character))
            return;

        lock (_bufferLock)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastKey = now - _lastKeystrokeTime;

            // AC2: Check if keystroke is rapid (< 100ms) or first keystroke
            if (_inputBuffer.Length == 0 || timeSinceLastKey < _interKeystrokeTimeout)
            {
                // Rapid keystroke - likely from scanner
                _inputBuffer.Append(character);
                _lastKeystrokeTime = now;

                // Reset timeout timer
                _timeoutTimer?.Dispose();
                _timeoutTimer = new System.Threading.Timer(
                    OnInputTimeout,
                    null,
                    _interKeystrokeTimeout,
                    Timeout.InfiniteTimeSpan);

                _logger.LogDebug("Buffered character (buffer length: {Length})", _inputBuffer.Length);
            }
            else
            {
                // AC6: Slow keystroke - discard previous buffer and start fresh
                _logger.LogDebug("Slow keystroke detected ({Delay}ms), discarding buffer", timeSinceLastKey.TotalMilliseconds);
                _inputBuffer.Clear();
                _inputBuffer.Append(character);
                _lastKeystrokeTime = now;

                // Start new timeout
                _timeoutTimer?.Dispose();
                _timeoutTimer = new System.Threading.Timer(
                    OnInputTimeout,
                    null,
                    _interKeystrokeTimeout,
                    Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// AC3: Processes Enter key - completes payload immediately.
    /// </summary>
    public void ProcessEnterKey()
    {
        if (!IsScanning)
            return;

        lock (_bufferLock)
        {
            _timeoutTimer?.Dispose();
            _timeoutTimer = null;

            if (_inputBuffer.Length > 0)
            {
                var payload = _inputBuffer.ToString();
                _inputBuffer.Clear();

                _logger.LogInformation("Enter key received, processing payload: {Payload}", payload);
                _ = ProcessPayloadAsync(payload);
            }
        }
    }

    /// <summary>
    /// AC4: Timeout handler - checks for valid SMARTLOG pattern.
    /// </summary>
    private void OnInputTimeout(object? state)
    {
        lock (_bufferLock)
        {
            if (_inputBuffer.Length == 0)
                return;

            var payload = _inputBuffer.ToString();
            _inputBuffer.Clear();

            // AC4: Only process if it starts with SMARTLOG:
            if (payload.StartsWith("SMARTLOG:", StringComparison.Ordinal))
            {
                _logger.LogInformation("Timeout - valid SMARTLOG pattern detected, processing: {Payload}", payload);
                _ = ProcessPayloadAsync(payload);
            }
            else
            {
                _logger.LogDebug("Timeout - discarding non-scanner input: {Payload}", payload);
            }
        }
    }

    /// <summary>
    /// Public API for testing - simulates a complete USB scan.
    /// </summary>
    public Task ProcessQrCodeAsync(string payload)
    {
        return ProcessPayloadAsync(payload);
    }

    /// <summary>
    /// AC5/US0017: Forwards complete payload to HMAC validation and server submission with online/offline routing.
    /// </summary>
    private async Task ProcessPayloadAsync(string payload)
    {
        var validationResult = await _hmacValidator.ValidateAsync(payload);

        ScanResult scanResult;

        if (validationResult.IsValid)
        {
            _logger.LogInformation("Valid USB scan - StudentId: {StudentId}", validationResult.StudentId);

            var scanType = _preferences.GetDefaultScanType();
            var scannedAt = _timeService.UtcNow;

            // Student-level deduplication check (after HMAC validation)
            var dedupResult = _dedup.CheckAndRecord(
                validationResult.StudentId!,
                scanType,
                studentName: null); // Name will be populated by server response

            switch (dedupResult.Action)
            {
                case DeduplicationAction.SuppressSilent:
                    // Within 2s suppress window - no UI feedback, no event raised
                    _logger.LogDebug("USB scan suppressed silently (within {Ms}ms of last scan)",
                        dedupResult.TimeSinceLastScan.TotalMilliseconds);
                    return;

                case DeduplicationAction.RejectWithFeedback:
                    // Within 30s warn window - show amber feedback, no API call
                    _logger.LogDebug("USB scan rejected with feedback (within {Sec}s of last scan)",
                        dedupResult.TimeSinceLastScan.TotalSeconds);

                    scanResult = new ScanResult
                    {
                        RawPayload = payload,
                        ValidationResult = validationResult,
                        Status = ScanStatus.DebouncedLocally,
                        Message = dedupResult.Message ?? "Already scanned. Please proceed.",
                        StudentId = validationResult.StudentId,
                        ScannedAt = scannedAt,
                        Source = ScanSource.UsbScanner
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
                _logger.LogDebug("Server online - submitting USB scan to API");
                scanResult = await _scanApi.SubmitScanAsync(payload, scannedAt, scanType, cameraName: "USB Scanner");

                // US0017 AC3: Mid-request failure fallback to queue (also covers rate limiting)
                if (scanResult.Status == ScanStatus.Queued || scanResult.Status == ScanStatus.Error
                    || scanResult.Status == ScanStatus.RateLimited)
                {
                    _logger.LogWarning("Server submission failed, falling back to offline queue");

                    try
                    {
                        // Check if already queued before enqueuing
                        var alreadyQueued = await _offlineQueue.HasPendingForStudentAsync(
                            validationResult.StudentId!, scanType);

                        if (!alreadyQueued)
                        {
                            await _offlineQueue.EnqueueScanAsync(payload, scannedAt, scanType, cameraName: "USB Scanner");
                        }
                        else
                        {
                            _logger.LogDebug("USB scan already queued for student {StudentId}, skipping duplicate enqueue",
                                validationResult.StudentId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to enqueue USB scan to offline queue");
                    }

                    scanResult = new ScanResult
                    {
                        RawPayload = payload,
                        ValidationResult = validationResult,
                        Status = ScanStatus.Queued,
                        Message = "Scan queued (offline)",
                        ScannedAt = scannedAt,
                        Source = ScanSource.UsbScanner
                    };
                }
                else
                {
                    // Preserve validation result and mark source for compatibility
                    scanResult = scanResult with { ValidationResult = validationResult, Source = ScanSource.UsbScanner };
                }
            }
            else
            {
                // US0017 AC2: Offline path - queue directly without attempting server submission
                _logger.LogDebug("Server offline - queueing USB scan locally");

                try
                {
                    // Check if already queued for this student+scanType
                    var alreadyQueued = await _offlineQueue.HasPendingForStudentAsync(
                        validationResult.StudentId!, scanType);

                    if (alreadyQueued)
                    {
                        // Already in queue - show amber feedback instead of blue
                        _logger.LogDebug("USB scan already queued for student {StudentId}, scan type {ScanType}",
                            validationResult.StudentId, scanType);

                        scanResult = new ScanResult
                        {
                            RawPayload = payload,
                            ValidationResult = validationResult,
                            Status = ScanStatus.DebouncedLocally,
                            Message = "Already queued. Please proceed.",
                            StudentId = validationResult.StudentId,
                            ScannedAt = scannedAt,
                            Source = ScanSource.UsbScanner
                        };
                    }
                    else
                    {
                        // Not queued yet - enqueue it
                        await _offlineQueue.EnqueueScanAsync(payload, scannedAt, scanType, cameraName: "USB Scanner");

                        scanResult = new ScanResult
                        {
                            RawPayload = payload,
                            ValidationResult = validationResult,
                            Status = ScanStatus.Queued,
                            Message = "Scan queued (offline)",
                            ScannedAt = scannedAt,
                            Source = ScanSource.UsbScanner
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue USB scan to offline queue");

                    scanResult = new ScanResult
                    {
                        RawPayload = payload,
                        ValidationResult = validationResult,
                        Status = ScanStatus.Error,
                        Message = "Failed to save scan",
                        ScannedAt = scannedAt,
                        Source = ScanSource.UsbScanner
                    };
                }
            }
        }
        else
        {
            _logger.LogWarning("Invalid USB scan - Reason: {Reason}", validationResult.RejectionReason);

            // Invalid HMAC - don't submit to server
            scanResult = new ScanResult
            {
                RawPayload = payload,
                ValidationResult = validationResult,
                Status = ScanStatus.Rejected,
                Message = validationResult.RejectionReason,
                ScannedAt = DateTimeOffset.UtcNow,
                Source = ScanSource.UsbScanner
            };
        }

        // Raise event for UI
        ScanCompleted?.Invoke(this, scanResult);
    }
}
