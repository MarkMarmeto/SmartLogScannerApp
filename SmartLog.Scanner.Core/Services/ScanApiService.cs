using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0010: Service for submitting validated scans to the SmartLog server API.
/// Handles all response types, rate limiting, and offline fallback.
/// </summary>
public class ScanApiService : IScanApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecureConfigService _secureConfig;
    private readonly IPreferencesService _preferences;
    private readonly IOfflineQueueService _offlineQueue;
    private readonly ILogger<ScanApiService> _logger;

    // AC10: Client-side rate tracking (120 requests per minute — matches server capacity)
    private readonly ConcurrentQueue<DateTimeOffset> _requestTimestamps = new();
    private const int MaxRequestsPerMinute = 120;
    private readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ScanApiService(
        IHttpClientFactory httpClientFactory,
        ISecureConfigService secureConfig,
        IPreferencesService preferences,
        IOfflineQueueService offlineQueue,
        ILogger<ScanApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secureConfig = secureConfig;
        _preferences = preferences;
        _offlineQueue = offlineQueue;
        _logger = logger;
    }

    /// <summary>
    /// AC1-AC10: Submits a validated QR scan to the server.
    /// </summary>
    public async Task<ScanResult> SubmitScanAsync(
        string qrPayload,
        DateTimeOffset scannedAt,
        string scanType,
        int? cameraIndex = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // ALWAYS ONLINE MODE: Rate limit check disabled, no queueing
            // Just continue to submit (server will enforce rate limits)
            if (IsRateLimitExceeded())
            {
                _logger.LogWarning("Client-side rate limit approached - submitting anyway (always-online mode)");
                // Continue to submit instead of queueing
            }

            // SECURITY FIX (CRITICAL-01): Retrieve API key from SecureStorage ONLY
            // Secrets are no longer stored in file config for security reasons
            var apiKey = await _secureConfig.GetApiKeyAsync();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogError("API key not configured in SecureStorage");
                return new ScanResult
                {
                    RawPayload = qrPayload,
                    Status = ScanStatus.Error,
                    ErrorReason = "MissingApiKey",
                    Message = "Device API key not configured. Please run device setup.",
                    ScannedAt = scannedAt,
                    ScanType = scanType
                };
            }

            // Load server URL from preferences (non-sensitive data)
            var serverUrl = _preferences.GetServerBaseUrl();

            // Check server URL is configured
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                _logger.LogError("Server URL not configured");
                return new ScanResult
                {
                    RawPayload = qrPayload,
                    Status = ScanStatus.Error,
                    ErrorReason = "MissingServerUrl",
                    Message = "Server URL not configured. Please run device setup.",
                    ScannedAt = scannedAt,
                    ScanType = scanType
                };
            }

            // AC7/AC8: Create HTTP client respecting current self-signed cert preference
            HttpClient httpClient;
            HttpClientHandler? tempHandler = null;
            var acceptSelfSigned = _preferences.GetAcceptSelfSignedCerts();

            if (acceptSelfSigned)
            {
                tempHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
                httpClient = new HttpClient(tempHandler) { Timeout = TimeSpan.FromSeconds(10) };
            }
            else
            {
                httpClient = _httpClientFactory.CreateClient("SmartLogApi");
            }

            httpClient.BaseAddress = new Uri(serverUrl);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Build request
            var requestBody = new
            {
                qrPayload,
                scannedAt = scannedAt.ToString("o"), // ISO 8601 format
                scanType,
                cameraIndex  // null for single-camera devices; server stores as nullable
            };

            var requestJson = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/scans")
            {
                Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json")
            };

            // AC9: Add API key header
            request.Headers.Add("X-API-Key", apiKey);

            _logger.LogInformation("Submitting scan to server: {QrPayload}, ScanType: {ScanType}",
                qrPayload, scanType);

            // Send request
            var response = await httpClient.SendAsync(request, linkedCts.Token);

            // Track request timestamp for rate limiting
            _requestTimestamps.Enqueue(DateTimeOffset.UtcNow);

            // AC1-AC5: Handle response based on status code
            return response.StatusCode switch
            {
                HttpStatusCode.OK => await ParseSuccessResponseAsync(response, qrPayload),
                HttpStatusCode.BadRequest => await ParseRejectedResponseAsync(response, qrPayload, scannedAt, scanType),
                HttpStatusCode.Unauthorized => ParseUnauthorizedResponse(qrPayload, scannedAt, scanType),
                (HttpStatusCode)429 => await ParseRateLimitedResponseAsync(response, qrPayload, scannedAt, scanType),
                _ => await HandleUnexpectedResponseAsync(response, qrPayload, scannedAt, scanType)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // AC6: Network error or timeout - queue offline
            return await HandleNetworkErrorAsync(qrPayload, scannedAt, scanType, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error submitting scan");
            return new ScanResult
            {
                RawPayload = qrPayload,
                Status = ScanStatus.Error,
                Message = "An unexpected error occurred. Please contact IT administrator.",
                ScannedAt = scannedAt,
                ScanType = scanType
            };
        }
    }

    /// <summary>
    /// AC1/AC2: Parse successful response (ACCEPTED or DUPLICATE).
    /// </summary>
    private async Task<ScanResult> ParseSuccessResponseAsync(HttpResponseMessage response, string qrPayload)
    {
        try
        {
            var responseJson = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                _logger.LogError("Empty response body from server (200 OK with no body)");
                return new ScanResult
                {
                    RawPayload = qrPayload,
                    Status = ScanStatus.Error,
                    ErrorReason = "EmptyResponse",
                    Message = "Something went wrong — please try again"
                };
            }

            var responseData = JsonSerializer.Deserialize<ServerResponse>(responseJson, _jsonOptions);

            if (responseData == null)
            {
                _logger.LogError("Failed to deserialize server response: {Json}", responseJson[..Math.Min(200, responseJson.Length)]);
                return new ScanResult
                {
                    RawPayload = qrPayload,
                    Status = ScanStatus.Error,
                    ErrorReason = "InvalidResponse",
                    Message = "Something went wrong — please try again"
                };
            }

            // SECURITY FIX (HIGH-03): Validate and sanitize all response data
            return ValidateAndBuildScanResult(responseData, qrPayload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed JSON in server response");
            return new ScanResult
            {
                RawPayload = qrPayload,
                Status = ScanStatus.Error,
                ErrorReason = "MalformedResponse",
                Message = "Something went wrong — please try again"
            };
        }
    }

    /// <summary>
    /// AC3: Parse rejected response (400 Bad Request).
    /// </summary>
    private async Task<ScanResult> ParseRejectedResponseAsync(
        HttpResponseMessage response,
        string qrPayload,
        DateTimeOffset scannedAt,
        string scanType)
    {
        try
        {
            var responseJson = await response.Content.ReadAsStringAsync();
            var errorData = JsonSerializer.Deserialize<ServerErrorResponse>(responseJson, _jsonOptions);

            _logger.LogWarning("Scan rejected: {ErrorReason} - {Message}",
                errorData?.Error, errorData?.Message);

            return new ScanResult
            {
                RawPayload = qrPayload,
                Status = ScanStatus.Rejected,
                ErrorReason = errorData?.Error,
                Message = errorData?.Message ?? "QR code rejected by server.",
                ScannedAt = scannedAt,
                ScanType = scanType
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse rejection response");
            return new ScanResult
            {
                RawPayload = qrPayload,
                Status = ScanStatus.Rejected,
                Message = "QR code rejected by server.",
                ScannedAt = scannedAt,
                ScanType = scanType
            };
        }
    }

    /// <summary>
    /// AC4: Parse unauthorized response (401).
    /// </summary>
    private ScanResult ParseUnauthorizedResponse(string qrPayload, DateTimeOffset scannedAt, string scanType)
    {
        _logger.LogError("API key invalid or expired (401 Unauthorized)");
        return new ScanResult
        {
            RawPayload = qrPayload,
            Status = ScanStatus.Error,
            ErrorReason = "InvalidApiKey",
            Message = "API key is invalid. Please contact your IT administrator to re-register this device.",
            ScannedAt = scannedAt,
            ScanType = scanType
        };
    }

    /// <summary>
    /// AC5: Parse rate limited response (429).
    /// </summary>
    private async Task<ScanResult> ParseRateLimitedResponseAsync(
        HttpResponseMessage response,
        string qrPayload,
        DateTimeOffset scannedAt,
        string scanType)
    {
        int retryAfterSeconds = 60; // Default

        if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
        {
            var retryAfterValue = retryAfterValues.FirstOrDefault();
            if (int.TryParse(retryAfterValue, out var parsedValue))
            {
                retryAfterSeconds = Math.Min(parsedValue, 300); // Cap at 5 minutes

                if (parsedValue > 300)
                {
                    _logger.LogWarning("Server requested unusually long Retry-After: {Seconds}s, capping at 300s",
                        parsedValue);
                }
            }
        }

        _logger.LogWarning("Rate limited by server (429) - Retry after {Seconds}s", retryAfterSeconds);

        // ALWAYS ONLINE MODE: No queueing, return error
        return new ScanResult
        {
            RawPayload = qrPayload,
            Status = ScanStatus.RateLimited,
            RetryAfterSeconds = retryAfterSeconds,
            Message = $"Too many scans — please wait {retryAfterSeconds}s",
            ScannedAt = scannedAt,
            ScanType = scanType
        };
    }

    /// <summary>
    /// Handle unexpected response status codes (5xx, etc.).
    /// </summary>
    private async Task<ScanResult> HandleUnexpectedResponseAsync(
        HttpResponseMessage response,
        string qrPayload,
        DateTimeOffset scannedAt,
        string scanType)
    {
        _logger.LogWarning("Unexpected server response: HTTP {StatusCode}", (int)response.StatusCode);

        // ALWAYS ONLINE MODE: No queueing, return error
        return new ScanResult
        {
            RawPayload = qrPayload,
            Status = ScanStatus.Error,
            ErrorReason = $"HttpError{(int)response.StatusCode}",
            Message = "Something went wrong — please try again",
            ScannedAt = scannedAt,
            ScanType = scanType
        };
    }

    /// <summary>
    /// AC6/AC7: Handle network errors and timeouts.
    /// </summary>
    private async Task<ScanResult> HandleNetworkErrorAsync(
        string qrPayload,
        DateTimeOffset scannedAt,
        string scanType,
        Exception ex,
        CancellationToken cancellationToken)
    {
        // Check if cancellation was requested by caller (not timeout)
        if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Scan submission cancelled by caller");
            return new ScanResult
            {
                RawPayload = qrPayload,
                Status = ScanStatus.Error,
                ErrorReason = "Cancelled",
                Message = "Scan cancelled",
                ScannedAt = scannedAt,
                ScanType = scanType
            };
        }

        // ALWAYS ONLINE MODE: Network error - return error, no queueing
        _logger.LogWarning(ex, "Network error during scan submission: {ExceptionType} - {Message}",
            ex.GetType().Name, ex.Message);

        return new ScanResult
        {
            RawPayload = qrPayload,
            Status = ScanStatus.Error,
            ErrorReason = "NetworkError",
            Message = "No connection — check your network",
            ScannedAt = scannedAt,
            ScanType = scanType
        };
    }

    /// <summary>
    /// AC10: Check if client-side rate limit would be exceeded.
    /// Uses sliding window of 60 requests per minute.
    /// </summary>
    private bool IsRateLimitExceeded()
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - RateLimitWindow;

        // Clean up old timestamps
        while (_requestTimestamps.TryPeek(out var timestamp) && timestamp < windowStart)
        {
            _requestTimestamps.TryDequeue(out _);
        }

        // Check if at limit
        return _requestTimestamps.Count >= MaxRequestsPerMinute;
    }

    /// <summary>
    /// SECURITY FIX (HIGH-03): Validate and sanitize server response data.
    /// Prevents XSS, injection, and DoS attacks via malicious server responses.
    /// </summary>
    private ScanResult ValidateAndBuildScanResult(
        ServerResponse response,
        string qrPayload,
        DateTimeOffset? scannedAt = null)
    {
        // Maximum string lengths to prevent DoS attacks
        const int MaxNameLength = 200;
        const int MaxGradeLength = 20;
        const int MaxSectionLength = 10;
        const int MaxMessageLength = 500;
        const int MaxScanIdLength = 100;

        // Validate and truncate StudentName
        var studentName = response.StudentName;
        if (studentName?.Length > MaxNameLength)
        {
            _logger.LogWarning("StudentName exceeds max length ({Length} > {Max}). Truncating.",
                studentName.Length, MaxNameLength);
            studentName = studentName.Substring(0, MaxNameLength);
        }

        // Validate and truncate Message
        var message = response.Message;
        if (message?.Length > MaxMessageLength)
        {
            _logger.LogWarning("Message exceeds max length ({Length} > {Max}). Truncating.",
                message.Length, MaxMessageLength);
            message = message.Substring(0, MaxMessageLength);
        }

        // Validate StudentId format (alphanumeric + hyphen, max 50 chars)
        var studentId = response.StudentId;
        if (!string.IsNullOrEmpty(studentId))
        {
            if (studentId.Length > 50)
            {
                _logger.LogWarning("StudentId exceeds max length ({Length} > 50). Truncating.",
                    studentId.Length);
                studentId = studentId.Substring(0, 50);
            }

            if (!Regex.IsMatch(studentId, @"^[A-Za-z0-9\-]+$"))
            {
                _logger.LogWarning("Invalid StudentId format from server: {StudentId}. Contains invalid characters.",
                    studentId);
                // Replace invalid characters with hyphen
                studentId = Regex.Replace(studentId, @"[^A-Za-z0-9\-]", "-");
            }
        }

        // Validate LRN (12 digits, optional)
        var lrn = response.Lrn;
        if (!string.IsNullOrEmpty(lrn) && lrn.Length > 12)
        {
            _logger.LogWarning("LRN exceeds max length ({Length} > 12). Truncating.", lrn.Length);
            lrn = lrn.Substring(0, 12);
        }

        // Validate and truncate Grade
        var grade = response.Grade;
        if (grade?.Length > MaxGradeLength)
        {
            _logger.LogWarning("Grade exceeds max length ({Length} > {Max}). Truncating.",
                grade.Length, MaxGradeLength);
            grade = grade.Substring(0, MaxGradeLength);
        }

        // Validate and truncate Section
        var section = response.Section;
        if (section?.Length > MaxSectionLength)
        {
            _logger.LogWarning("Section exceeds max length ({Length} > {Max}). Truncating.",
                section.Length, MaxSectionLength);
            section = section.Substring(0, MaxSectionLength);
        }

        // Validate and truncate ScanId
        var scanId = response.ScanId;
        if (scanId?.Length > MaxScanIdLength)
        {
            _logger.LogWarning("ScanId exceeds max length ({Length} > {Max}). Truncating.",
                scanId.Length, MaxScanIdLength);
            scanId = scanId.Substring(0, MaxScanIdLength);
        }

        // Validate and truncate OriginalScanId
        var originalScanId = response.OriginalScanId;
        if (originalScanId?.Length > MaxScanIdLength)
        {
            _logger.LogWarning("OriginalScanId exceeds max length ({Length} > {Max}). Truncating.",
                originalScanId.Length, MaxScanIdLength);
            originalScanId = originalScanId.Substring(0, MaxScanIdLength);
        }

        // Validate Status enum (whitelist)
        var status = ValidateStatus(response.Status);

        _logger.LogInformation("Validated scan {Status}: StudentId={StudentId}, StudentName={StudentName}",
            status, studentId, studentName);

        return new ScanResult
        {
            RawPayload = qrPayload,
            Status = status,
            ScanId = scanId,
            StudentId = studentId,
            Lrn = lrn,
            StudentName = studentName,
            Grade = grade,
            Section = section,
            ScanType = response.ScanType,
            ScannedAt = response.ScannedAt ?? scannedAt ?? DateTimeOffset.UtcNow,
            OriginalScanId = originalScanId,
            Message = message,
            PassCode = response.PassCode,
            PassNumber = response.PassNumber
        };
    }

    /// <summary>
    /// SECURITY FIX (HIGH-03): Validate status enum value against whitelist.
    /// </summary>
    private ScanStatus ValidateStatus(string? status)
    {
        return status?.ToUpperInvariant() switch
        {
            "ACCEPTED" => ScanStatus.Accepted,
            "DUPLICATE" => ScanStatus.Duplicate,
            "REJECTED" => ScanStatus.Rejected,
            "REJECTED_PASS_INACTIVE" => ScanStatus.Rejected,
            _ => ScanStatus.Error
        };
    }

    #region Response Models

    private class ServerResponse
    {
        public string? ScanId { get; set; }
        public string? StudentId { get; set; }
        public string? Lrn { get; set; }
        public string? StudentName { get; set; }
        public string? Grade { get; set; }
        public string? Section { get; set; }
        public string? ScanType { get; set; }
        public DateTimeOffset? ScannedAt { get; set; }
        public string? Status { get; set; }
        public string? OriginalScanId { get; set; }
        public string? Message { get; set; }
        // US0076: Visitor pass fields (null for student scans)
        public string? PassCode { get; set; }
        public int? PassNumber { get; set; }
    }

    private class ServerErrorResponse
    {
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? Status { get; set; }
    }

    #endregion
}
