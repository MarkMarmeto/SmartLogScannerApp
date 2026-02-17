using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
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
    private readonly FileConfigService _fileConfig;
    private readonly IOfflineQueueService _offlineQueue;
    private readonly ILogger<ScanApiService> _logger;

    // AC10: Client-side rate tracking (60 requests per minute sliding window)
    private readonly ConcurrentQueue<DateTimeOffset> _requestTimestamps = new();
    private const int MaxRequestsPerMinute = 60;
    private readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ScanApiService(
        IHttpClientFactory httpClientFactory,
        ISecureConfigService secureConfig,
        FileConfigService fileConfig,
        IOfflineQueueService offlineQueue,
        ILogger<ScanApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secureConfig = secureConfig;
        _fileConfig = fileConfig;
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            // AC10: Check client-side rate limit before submitting
            if (IsRateLimitExceeded())
            {
                _logger.LogWarning("Client-side rate limit approached - queuing scan offline");
                await _offlineQueue.EnqueueScanAsync(qrPayload, scannedAt, scanType);
                return new ScanResult
                {
                    RawPayload = qrPayload,
                    Status = ScanStatus.Queued,
                    Message = "Rate limit approached - scan queued",
                    ScannedAt = scannedAt,
                    ScanType = scanType
                };
            }

            // AC9: Retrieve API key from secure config
            var config = await _fileConfig.LoadConfigAsync();
            var apiKey = config.ApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = await _secureConfig.GetApiKeyAsync();
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogError("API key not configured");
                return new ScanResult
                {
                    RawPayload = qrPayload,
                    Status = ScanStatus.Error,
                    ErrorReason = "MissingApiKey",
                    Message = "Device API key not configured. Please contact IT administrator.",
                    ScannedAt = scannedAt,
                    ScanType = scanType
                };
            }

            // Check server URL is configured
            if (string.IsNullOrWhiteSpace(config.ServerUrl))
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

            // AC7/AC8: Create HTTP client with 10-second timeout
            var httpClient = _httpClientFactory.CreateClient("SmartLogApi");
            httpClient.BaseAddress = new Uri(config.ServerUrl);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Build request
            var requestBody = new
            {
                qrPayload,
                scannedAt = scannedAt.ToString("o"), // ISO 8601 format
                scanType
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
                _logger.LogError("Empty response body from server");
                return new ScanResult
                {
                    RawPayload = qrPayload,
                    Status = ScanStatus.Error,
                    Message = "Empty server response. Please contact IT administrator."
                };
            }

            var responseData = JsonSerializer.Deserialize<ServerResponse>(responseJson, _jsonOptions);

            if (responseData == null)
            {
                _logger.LogError("Failed to deserialize server response");
                return new ScanResult
                {
                    RawPayload = qrPayload,
                    Status = ScanStatus.Error,
                    Message = "Invalid server response. Please contact IT administrator."
                };
            }

            var status = responseData.Status?.ToUpperInvariant() switch
            {
                "ACCEPTED" => ScanStatus.Accepted,
                "DUPLICATE" => ScanStatus.Duplicate,
                _ => ScanStatus.Error
            };

            _logger.LogInformation("Scan {Status}: StudentId={StudentId}, StudentName={StudentName}",
                status, responseData.StudentId, responseData.StudentName);

            return new ScanResult
            {
                RawPayload = qrPayload,
                Status = status,
                ScanId = responseData.ScanId,
                StudentId = responseData.StudentId,
                StudentName = responseData.StudentName,
                Grade = responseData.Grade,
                Section = responseData.Section,
                ScanType = responseData.ScanType,
                ScannedAt = responseData.ScannedAt ?? DateTimeOffset.UtcNow,
                OriginalScanId = responseData.OriginalScanId,
                Message = responseData.Message
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed JSON response from server");
            return new ScanResult
            {
                RawPayload = qrPayload,
                Status = ScanStatus.Error,
                Message = "Invalid server response. Please contact IT administrator."
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

        // Queue the scan offline rather than waiting
        await _offlineQueue.EnqueueScanAsync(qrPayload, scannedAt, scanType);

        return new ScanResult
        {
            RawPayload = qrPayload,
            Status = ScanStatus.RateLimited,
            RetryAfterSeconds = retryAfterSeconds,
            Message = $"Rate limit exceeded. Scan queued for retry.",
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
        _logger.LogWarning("Unexpected server response: {StatusCode}", response.StatusCode);

        // Treat as network error - queue offline
        await _offlineQueue.EnqueueScanAsync(qrPayload, scannedAt, scanType);

        return new ScanResult
        {
            RawPayload = qrPayload,
            Status = ScanStatus.Queued,
            Message = "Server error - scan queued (offline)",
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
                Message = "Scan submission cancelled",
                ScannedAt = scannedAt,
                ScanType = scanType
            };
        }

        // Network error or timeout - queue offline
        _logger.LogWarning(ex, "Network error during scan submission - queuing offline");

        try
        {
            await _offlineQueue.EnqueueScanAsync(qrPayload, scannedAt, scanType);

            return new ScanResult
            {
                RawPayload = qrPayload,
                Status = ScanStatus.Queued,
                Message = "Scan queued (offline)",
                ScannedAt = scannedAt,
                ScanType = scanType
            };
        }
        catch (Exception queueEx)
        {
            _logger.LogError(queueEx, "Failed to queue scan offline - critical failure");
            return new ScanResult
            {
                RawPayload = qrPayload,
                Status = ScanStatus.Error,
                Message = "Failed to queue scan offline. Please contact IT administrator.",
                ScannedAt = scannedAt,
                ScanType = scanType
            };
        }
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

    #region Response Models

    private class ServerResponse
    {
        public string? ScanId { get; set; }
        public string? StudentId { get; set; }
        public string? StudentName { get; set; }
        public string? Grade { get; set; }
        public string? Section { get; set; }
        public string? ScanType { get; set; }
        public DateTimeOffset? ScannedAt { get; set; }
        public string? Status { get; set; }
        public string? OriginalScanId { get; set; }
        public string? Message { get; set; }
    }

    private class ServerErrorResponse
    {
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? Status { get; set; }
    }

    #endregion
}
