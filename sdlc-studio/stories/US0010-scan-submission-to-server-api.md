# US0010: Implement Scan Submission to Server API

> **Status:** Draft
> **Epic:** [EP0003: Scan Processing and Feedback](../epics/EP0003-scan-processing-and-feedback.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As a** system (benefiting Guard Gary who sees the results)
**I want** a scan submission service that sends validated QR scan data to the server API and correctly handles all response types
**So that** each scanned QR code is recorded on the server with accurate student information returned for display, and failures are handled gracefully with appropriate feedback or offline queuing

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency. Does not interact with this service directly but sees its output as the color-coded scan result. Expects instant feedback -- green for accepted, amber for duplicate, red for rejected, blue for queued offline.
[Full persona details](../personas.md#guard-gary)

### Background
After a QR code is scanned and validated locally (HMAC verification in EP0002), the scan data must be submitted to the SmartLog Admin Web App server via POST /api/v1/scans. The server responds with student information and a status (ACCEPTED, DUPLICATE, or REJECTED), which drives the UI feedback Guard Gary sees. This is the most critical integration point in the application -- it bridges local scanning with server-side attendance recording. The service must handle every possible server response (200, 400, 401, 429) and network failure scenario, falling back to offline queuing (IOfflineQueueService from EP0004) when the server is unreachable. HttpClient is obtained from IHttpClientFactory with Polly retry and circuit breaker policies configured in the DI pipeline.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Performance | Scan-to-result feedback < 500ms (excluding network latency) | Serialization, HTTP setup, and response parsing must be minimal overhead |
| TRD | Tech Stack | HttpClient via IHttpClientFactory("SmartLogApi") with Polly policies | Must not create raw HttpClient instances; must use named factory client |
| TRD | Architecture | Interface-based services for DI and testability | IScanApiService interface with ScanResult return type; mockable in unit tests |
| PRD | Security | API key sent as X-API-Key header | Key retrieved from ISecureConfigService.GetApiKeyAsync() per request |
| TRD | Resilience | Polly retry (3 attempts, exponential backoff) + circuit breaker (5 failures, 30s break) | Polly policies configured at IHttpClientFactory registration; service code uses HttpClient normally |
| Epic | Reliability | Network failure triggers offline queue handoff | Must detect network errors and delegate to IOfflineQueueService |

---

## Acceptance Criteria

### AC1: Successful scan submission with ACCEPTED response
- **Given** a validated QR payload "SMARTLOG:v1:1001:1700000000:abc123:sig" and scan type "ENTRY"
- **When** IScanApiService.SubmitScanAsync(qrPayload, scannedAt, scanType) is called
- **Then** a POST request is sent to {baseUrl}/api/v1/scans with header "X-API-Key: {device-api-key}" and JSON body `{ "qrPayload": "SMARTLOG:v1:1001:1700000000:abc123:sig", "scannedAt": "2026-02-13T07:30:00Z", "scanType": "ENTRY" }`, and when the server returns 200 with status "ACCEPTED", the method returns a ScanResult with Status=Accepted, StudentId="1001", StudentName="Maria Santos", Grade="Grade 7", Section="Section A", ScanType="ENTRY", ScannedAt matching the submitted time, and ScanId from the response

### AC2: Duplicate scan response handling
- **Given** a scan for a student who has already been scanned today
- **When** the server returns 200 with status "DUPLICATE"
- **Then** the method returns a ScanResult with Status=Duplicate, all student info fields populated, OriginalScanId set to the original scan's ID, and Message set to the server's duplicate message (e.g., "Already scanned. Please proceed.")

### AC3: Rejected scan response handling (400)
- **Given** a scan with an invalid or inactive QR code
- **When** the server returns 400 with error "StudentInactive"
- **Then** the method returns a ScanResult with Status=Rejected, ErrorReason="StudentInactive", and Message from the server (e.g., "Student account is inactive. Please contact the registrar.")

### AC4: Invalid API key response handling (401)
- **Given** the device API key is invalid or expired
- **When** the server returns 401 with error "InvalidApiKey"
- **Then** the method returns a ScanResult with Status=Error, ErrorReason="InvalidApiKey", and Message="API key is invalid. Please contact your IT administrator to re-register this device."

### AC5: Rate limiting response handling (429)
- **Given** the device has exceeded the server's rate limit
- **When** the server returns 429 with a Retry-After header of "30"
- **Then** the method returns a ScanResult with Status=RateLimited, RetryAfterSeconds=30, and the service internally tracks the rate limit state to delay subsequent requests until the Retry-After period has elapsed

### AC6: Network error triggers offline queue fallback
- **Given** the server is unreachable (network timeout, DNS failure, connection refused)
- **When** IScanApiService.SubmitScanAsync is called and the HTTP request fails with a network error
- **Then** the method delegates to IOfflineQueueService.EnqueueScanAsync(qrPayload, scannedAt, scanType), and returns a ScanResult with Status=Queued and Message="Scan queued (offline)"

### AC7: Request timeout of 10 seconds
- **Given** the server is reachable but responding slowly
- **When** the server does not respond within 10 seconds
- **Then** the request is cancelled via CancellationToken, treated as a network error, the scan is queued offline via IOfflineQueueService, and a ScanResult with Status=Queued is returned

### AC8: HttpClient obtained from IHttpClientFactory
- **Given** IScanApiService is instantiated via DI
- **When** the service needs to make an HTTP request
- **Then** it calls IHttpClientFactory.CreateClient("SmartLogApi") to obtain an HttpClient instance with pre-configured Polly policies (retry + circuit breaker) and base address

### AC9: API key retrieved per request
- **Given** the API key may change between requests (e.g., re-registration by IT Admin Ian)
- **When** SubmitScanAsync is called
- **Then** ISecureConfigService.GetApiKeyAsync() is called to retrieve the current API key, which is set as the X-API-Key header value on the request

### AC10: Client-side rate tracking
- **Given** the application is processing scans at a high rate
- **When** the submission rate approaches 60 requests per minute
- **Then** the service tracks submission timestamps and, if the rate would exceed 60/min, queues the scan offline rather than sending to the server, returning ScanResult with Status=Queued and Message="Rate limit approached - scan queued"

---

## Scope

### In Scope
- IScanApiService interface with SubmitScanAsync(string qrPayload, DateTimeOffset scannedAt, string scanType) returning Task<ScanResult>
- ScanApiService concrete implementation using IHttpClientFactory("SmartLogApi")
- ScanResult model with all fields (Status, ScanId, StudentId, StudentName, Grade, Section, ScanType, ScannedAt, OriginalScanId, Message, ErrorReason, RetryAfterSeconds)
- ScanStatus enum: Accepted, Duplicate, Rejected, Error, RateLimited, Queued
- JSON serialization/deserialization of request and response bodies (System.Text.Json)
- X-API-Key header injection from ISecureConfigService
- Response handling for 200 (ACCEPTED/DUPLICATE), 400 (REJECTED), 401, 429
- Network error detection and fallback to IOfflineQueueService.EnqueueScanAsync
- Client-side rate tracking (sliding window, max 60/min)
- 10-second request timeout via CancellationTokenSource
- DI registration in MauiProgram.cs (scoped or transient lifetime)
- Unit tests with Moq for IHttpClientFactory, ISecureConfigService, IOfflineQueueService

### Out of Scope
- Polly policy configuration (configured at IHttpClientFactory registration in EP0001/US0002)
- UI display of scan results (covered by US0011)
- Audio playback on scan result (covered by US0012)
- Offline queue storage implementation (covered by US0014 in EP0004)
- Background sync of queued scans (covered by US0015 in EP0004)
- QR payload validation/HMAC verification (covered by EP0002)
- Retry logic for individual failed scans (Polly handles transient retries at the HTTP level)

---

## Technical Notes

### Implementation Details
- **IScanApiService** interface:
  ```csharp
  public interface IScanApiService
  {
      Task<ScanResult> SubmitScanAsync(string qrPayload, DateTimeOffset scannedAt, string scanType, CancellationToken cancellationToken = default);
  }
  ```
- **ScanResult** model:
  ```csharp
  public record ScanResult
  {
      public ScanStatus Status { get; init; }
      public string? ScanId { get; init; }
      public string? StudentId { get; init; }
      public string? StudentName { get; init; }
      public string? Grade { get; init; }
      public string? Section { get; init; }
      public string? ScanType { get; init; }
      public DateTimeOffset? ScannedAt { get; init; }
      public string? OriginalScanId { get; init; }
      public string? Message { get; init; }
      public string? ErrorReason { get; init; }
      public int? RetryAfterSeconds { get; init; }
  }

  public enum ScanStatus
  {
      Accepted,
      Duplicate,
      Rejected,
      Error,
      RateLimited,
      Queued
  }
  ```
- **Request serialization** uses System.Text.Json with camelCase naming policy:
  ```csharp
  var requestBody = new
  {
      qrPayload,
      scannedAt = scannedAt.ToString("o"),
      scanType
  };
  var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
  ```
- **Rate tracking** uses a `ConcurrentQueue<DateTimeOffset>` sliding window. Before each submission, dequeue entries older than 60 seconds, then check if count >= 60.
- **Timeout** is implemented via a linked `CancellationTokenSource` combining the caller's token with a 10-second timeout token.
- **Error handling pattern:**
  ```csharp
  try
  {
      var response = await httpClient.SendAsync(request, linkedCts.Token);
      return response.StatusCode switch
      {
          HttpStatusCode.OK => await ParseSuccessResponseAsync(response),
          HttpStatusCode.BadRequest => await ParseRejectedResponseAsync(response),
          HttpStatusCode.Unauthorized => ParseUnauthorizedResponse(),
          (HttpStatusCode)429 => ParseRateLimitedResponse(response),
          _ => await HandleUnexpectedResponseAsync(response)
      };
  }
  catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
  {
      return await HandleNetworkErrorAsync(qrPayload, scannedAt, scanType, ex);
  }
  ```

### API Contracts

**Request:**
```
POST /api/v1/scans HTTP/1.1
Host: {serverBaseUrl}
Content-Type: application/json
X-API-Key: {device-api-key}

{
  "qrPayload": "SMARTLOG:v1:1001:1700000000:abc123:sig",
  "scannedAt": "2026-02-13T07:30:00.000Z",
  "scanType": "ENTRY"
}
```

**Response 200 - ACCEPTED:**
```json
{
  "scanId": "scan-uuid-001",
  "studentId": "1001",
  "studentName": "Maria Santos",
  "grade": "Grade 7",
  "section": "Section A",
  "scanType": "ENTRY",
  "scannedAt": "2026-02-13T07:30:00.000Z",
  "status": "ACCEPTED"
}
```

**Response 200 - DUPLICATE:**
```json
{
  "scanId": "scan-uuid-002",
  "studentId": "1001",
  "studentName": "Maria Santos",
  "grade": "Grade 7",
  "section": "Section A",
  "scanType": "ENTRY",
  "scannedAt": "2026-02-13T07:30:00.000Z",
  "status": "DUPLICATE",
  "originalScanId": "scan-uuid-001",
  "message": "Already scanned. Please proceed."
}
```

**Response 400 - REJECTED:**
```json
{
  "error": "StudentInactive",
  "message": "Student account is inactive. Please contact the registrar.",
  "status": "REJECTED"
}
```
Possible `error` values: `"InvalidQrCode"`, `"StudentInactive"`, `"QrCodeInvalidated"`

**Response 401 - Unauthorized:**
```json
{
  "error": "InvalidApiKey",
  "message": "The provided API key is not valid or has been revoked."
}
```

**Response 429 - Rate Limited:**
```
HTTP/1.1 429 Too Many Requests
Retry-After: 30

{
  "error": "TooManyRequests",
  "message": "Rate limit exceeded. Please slow down."
}
```

### Data Requirements

**ScanStatus Enum:**

| Value | HTTP Status | Server Status | Description |
|-------|-------------|---------------|-------------|
| Accepted | 200 | "ACCEPTED" | Student scan recorded successfully |
| Duplicate | 200 | "DUPLICATE" | Student already scanned; informational |
| Rejected | 400 | "REJECTED" | QR code invalid, student inactive, or QR invalidated |
| Error | 401 | N/A | API key invalid; device needs re-registration |
| RateLimited | 429 | N/A | Too many requests; retry after specified period |
| Queued | N/A | N/A | Network error; scan saved to offline queue |

**Request DTO:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| qrPayload | string | Yes | Full QR code string including HMAC signature |
| scannedAt | string (ISO 8601) | Yes | Timestamp of the scan in UTC |
| scanType | string | Yes | "ENTRY" or "EXIT" |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Server returns unexpected status code (e.g., 500, 502, 503) | Polly retry policy handles transient 5xx errors (up to 3 retries with exponential backoff); if all retries exhausted, treat as network error and queue offline; return ScanResult with Status=Queued |
| Malformed JSON response from server (200 status but invalid JSON body) | JsonException caught; log the raw response body (truncated to 500 chars) and error details; return ScanResult with Status=Error, Message="Invalid server response. Please contact IT administrator." |
| Empty response body (200 status, Content-Length: 0) | Detect empty body before deserialization; return ScanResult with Status=Error, Message="Empty server response. Please contact IT administrator." |
| Network drops mid-request (connection reset after partial send) | HttpRequestException caught; queue scan offline via IOfflineQueueService; return ScanResult with Status=Queued, Message="Scan queued (offline)" |
| Rate limit exactly at 60/min (60th request in the sliding window) | 60th request is allowed (limit is 60 per minute); 61st request is queued locally; sliding window uses >= 60 as the threshold to queue |
| 429 response with no Retry-After header | Default to 60 seconds retry delay; return ScanResult with Status=RateLimited, RetryAfterSeconds=60 |
| 429 response with very large Retry-After value (e.g., 86400 seconds / 24 hours) | Cap RetryAfterSeconds at 300 (5 minutes); log a warning that the server requested an unusually long retry period |
| Server returns 200 but response JSON has unexpected schema (missing required fields) | Deserialize with null-tolerant options; return ScanResult with available fields populated and missing fields as null; if status field is missing, treat as Error |
| Concurrent scan submissions (two scans submitted simultaneously) | Each call creates its own HttpRequestMessage and uses its own CancellationTokenSource; IHttpClientFactory provides thread-safe clients; rate tracker uses ConcurrentQueue for thread safety |
| Request cancelled by caller (CancellationToken triggered) | OperationCanceledException caught; if token was the caller's token (not timeout), do NOT queue offline; return ScanResult with Status=Error, Message="Scan submission cancelled" |
| API key changed between requests (IT Admin re-registers device) | GetApiKeyAsync() is called per request; the new key is used immediately on the next submission without requiring app restart |
| API key is null or empty (not configured) | Detect before sending request; return ScanResult with Status=Error, ErrorReason="MissingApiKey", Message="Device API key not configured. Please contact IT administrator." |
| Server URL not configured (empty base URL) | Detect before sending request; return ScanResult with Status=Error, ErrorReason="MissingServerUrl", Message="Server URL not configured. Please run device setup." |
| IOfflineQueueService.EnqueueScanAsync fails during offline fallback | Log the error; return ScanResult with Status=Error, Message="Failed to queue scan offline. Please contact IT administrator." -- this is a critical failure |

---

## Test Scenarios

- [ ] SubmitScanAsync sends POST to /api/v1/scans with correct JSON body (qrPayload, scannedAt in ISO 8601, scanType)
- [ ] SubmitScanAsync includes X-API-Key header with value from ISecureConfigService.GetApiKeyAsync()
- [ ] 200 ACCEPTED response parsed into ScanResult with Status=Accepted and all student fields populated
- [ ] 200 DUPLICATE response parsed into ScanResult with Status=Duplicate, OriginalScanId, and Message
- [ ] 400 REJECTED response with error "InvalidQrCode" parsed into ScanResult with Status=Rejected, ErrorReason="InvalidQrCode"
- [ ] 400 REJECTED response with error "StudentInactive" parsed correctly
- [ ] 400 REJECTED response with error "QrCodeInvalidated" parsed correctly
- [ ] 401 response returns ScanResult with Status=Error and ErrorReason="InvalidApiKey"
- [ ] 429 response with Retry-After: 30 returns ScanResult with Status=RateLimited, RetryAfterSeconds=30
- [ ] 429 response without Retry-After header defaults RetryAfterSeconds to 60
- [ ] 429 response with Retry-After > 300 caps RetryAfterSeconds at 300
- [ ] HttpRequestException (network error) triggers IOfflineQueueService.EnqueueScanAsync and returns Status=Queued
- [ ] TaskCanceledException from timeout triggers offline queue and returns Status=Queued
- [ ] Request timeout is 10 seconds (CancellationTokenSource with TimeSpan.FromSeconds(10))
- [ ] Caller-initiated cancellation returns Status=Error (not Queued) and does NOT enqueue offline
- [ ] Malformed JSON response (200 with invalid JSON) returns Status=Error with descriptive message
- [ ] Empty response body (200 with no content) returns Status=Error
- [ ] Unexpected status code (500) after Polly retries exhausted triggers offline queue
- [ ] Client-side rate tracker allows 60 requests in a 60-second window
- [ ] Client-side rate tracker queues the 61st request locally (returns Status=Queued)
- [ ] Concurrent submissions do not interfere with each other (parallel test with 5 simultaneous calls)
- [ ] API key is null: returns Status=Error with ErrorReason="MissingApiKey" without making HTTP call
- [ ] HttpClient obtained from IHttpClientFactory.CreateClient("SmartLogApi")
- [ ] IScanApiService registered in DI container and resolvable via constructor injection
- [ ] 200 response with missing studentName field returns ScanResult with StudentName=null (no exception)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0001 | Requires | ISecureConfigService for retrieving API key via GetApiKeyAsync() | Draft |
| US0002 | Requires | IHttpClientFactory("SmartLogApi") registration with Polly policies and base address | Planned |
| US0014 | Requires | IOfflineQueueService.EnqueueScanAsync() for offline fallback | Planned |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Microsoft.Extensions.Http (IHttpClientFactory) | NuGet Package | Available in .NET 8.0 |
| Microsoft.Extensions.Http.Polly | NuGet Package | Available; provides Polly integration with IHttpClientFactory |
| System.Text.Json | Platform SDK | Built into .NET 8.0 |
| SmartLog Admin Web App API (POST /api/v1/scans) | External API | Server must be deployed and accessible on school LAN |

---

## Estimation

**Story Points:** 8
**Complexity:** High

---

## Open Questions

- [ ] Should the circuit breaker state (open/half-open/closed) be exposed to the UI layer so Guard Gary sees a connectivity warning before attempting a scan? - Owner: Architect
- [ ] When Polly circuit breaker is open, should SubmitScanAsync immediately queue offline without attempting the request, or let Polly handle the fast-fail? - Owner: Architect
- [ ] Should the client-side rate limit (60/min) be configurable via appsettings.json or hardcoded? - Owner: Product
- [ ] How should the service behave if ISecureConfigService.GetApiKeyAsync() itself throws an exception (SecureStorage failure)? - Owner: Architect

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
