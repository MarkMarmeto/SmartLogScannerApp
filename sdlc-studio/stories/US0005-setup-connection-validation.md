# US0005: Implement Setup Connection Validation

> **Status:** Done
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As** IT Admin Ian
**I want** a "Test Connection" button on the setup page that validates server connectivity and API key correctness before saving
**So that** I know immediately whether the configuration is correct and can fix specific issues (wrong URL, bad API key, server down) before leaving the gate machine

## Context

### Persona Reference
**IT Admin Ian** - School IT administrator, intermediate technical proficiency. His biggest pain point is vague error messages that don't distinguish between a bad URL, a bad API key, and a server that's down. He needs specific, actionable error messages.
[Full persona details](../personas.md#it-admin-ian)

### Background
During device setup, IT Admin Ian enters the server URL and API key. Before committing the configuration, he needs to verify that the device can actually reach the server and that the API key is valid. The "Test Connection" button sends a GET request to /api/v1/health/details with the X-API-Key header. This authenticated endpoint confirms both network connectivity and API key validity. Different failure modes (connection refused, DNS failure, timeout, 401 unauthorized, TLS error) must produce specific, actionable error messages so Ian knows exactly what to fix. This story adds the connection test functionality to the SetupPage built in US0004.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | UX | Clear error messages that distinguish between bad URL, bad key, and server down | Must map each HTTP failure mode to a specific user-facing message |
| PRD | Feature | "Test Connection" validates via GET /api/v1/health/details with X-API-Key | Must use authenticated health endpoint, not unauthenticated /health |
| TRD | Resilience | 10-second HTTP timeout | Connection test must respect the configured timeout |
| TRD | Architecture | Named HttpClient "SmartLogApi" via IHttpClientFactory | Must use the configured HttpClient with TLS and Polly policies |
| PRD | Security | API key sent only over HTTPS (or HTTP for local dev) | Connection test transmits API key in X-API-Key header over the configured URL |

---

## Acceptance Criteria

### AC1: Test Connection button present on SetupPage
- **Given** SetupPage is displayed
- **When** IT Admin Ian views the form below the API Key field
- **Then** a "Test Connection" button is visible, styled as a secondary/outlined button, positioned between the input fields and the Save button

### AC2: Successful connection test (HTTP 200)
- **Given** IT Admin Ian has entered a valid server URL ("https://192.168.1.100:8443") and valid API key ("sk-device-001-abc123")
- **When** the "Test Connection" button is pressed
- **Then** a GET request is sent to "https://192.168.1.100:8443/api/v1/health/details" with header "X-API-Key: sk-device-001-abc123", the server responds with 200, a green checkmark icon and the message "Connection successful" are displayed, and the Save button becomes enabled

### AC3: Authentication error (HTTP 401)
- **Given** IT Admin Ian has entered a valid server URL but an invalid API key
- **When** the "Test Connection" button is pressed and the server responds with HTTP 401
- **Then** a red X icon and the message "Invalid API key. Please verify your API key from the admin panel." are displayed, and the Save button remains disabled

### AC4: Connection refused
- **Given** IT Admin Ian has entered a server URL where no server is listening (e.g., wrong port)
- **When** the "Test Connection" button is pressed and the HTTP request throws an HttpRequestException with an inner SocketException (ConnectionRefused)
- **Then** a red X icon and the message "Cannot reach server. Check the server URL and ensure the server is running." are displayed

### AC5: Connection timeout (10 seconds)
- **Given** IT Admin Ian has entered a server URL that is reachable but the server does not respond within 10 seconds
- **When** the "Test Connection" button is pressed and the request times out (TaskCanceledException)
- **Then** a red X icon and the message "Connection timed out. Check network connectivity." are displayed

### AC6: DNS resolution failure
- **Given** IT Admin Ian has entered a server URL with an unresolvable hostname (e.g., "https://nonexistent.local:8443")
- **When** the "Test Connection" button is pressed and DNS resolution fails (HttpRequestException with inner SocketException indicating name resolution failure)
- **Then** a red X icon and the message "Server not found. Check the URL format." are displayed

### AC7: TLS certificate error when self-signed certs not accepted
- **Given** AcceptSelfSignedCerts is set to false in appsettings.json and the server uses a self-signed certificate
- **When** the "Test Connection" button is pressed and the TLS handshake fails (HttpRequestException with inner AuthenticationException)
- **Then** a red X icon and the message "TLS certificate error. Enable self-signed certificate support." are displayed

### AC8: Loading state during connection test
- **Given** IT Admin Ian presses the "Test Connection" button
- **When** the HTTP request is in progress
- **Then** the button text changes to "Testing..." with a loading indicator (ActivityIndicator), the button is disabled to prevent duplicate clicks, and the Save button is disabled until the test completes

### AC9: Test results persist until next test or field change
- **Given** a connection test has completed (success or failure) and the result is displayed
- **When** IT Admin Ian does not modify any fields or press Test Connection again
- **Then** the test result (icon + message) remains visible on screen

### AC10: Test results clear on field change
- **Given** a connection test has completed and the result is displayed
- **When** IT Admin Ian modifies the Server URL or API Key field
- **Then** the previous test result is cleared, and the Save button returns to its default state (disabled until a new successful test, or enabled if the previous flow allowed it)

---

## Scope

### In Scope
- "Test Connection" button on SetupPage with binding to SetupViewModel.TestConnectionCommand
- ISetupConnectionService (or method on existing service) encapsulating the GET /api/v1/health/details call
- HTTP failure mode detection and mapping to specific user-facing error messages
- Loading state management (IsTestingConnection property on ViewModel)
- Test result display (ConnectionTestResult enum: None, Success, AuthError, ConnectionRefused, Timeout, DnsFailure, TlsError, UnexpectedError)
- Save button enabled/disabled based on connection test result
- Test result cleared when Server URL or API Key fields change
- Unit tests for all failure mode mappings using xUnit + Moq with mock HttpMessageHandler
- Integration consideration: Polly retry policies apply to the test connection request

### Out of Scope
- Automatic periodic re-testing
- Connection test for HMAC secret validation (HMAC is validated locally, not via server)
- Server version compatibility check
- Network speed or latency measurement
- Caching of connection test results across app restarts
- Testing from Settings page (setup-only for v1.0)

---

## Technical Notes

### Implementation Details

**SetupViewModel additions:**
```csharp
[ObservableProperty] private bool _isTestingConnection;
[ObservableProperty] private ConnectionTestResult _testResult = ConnectionTestResult.None;
[ObservableProperty] private string? _testResultMessage;
[ObservableProperty] private bool _isConnectionValid;

[RelayCommand(CanExecute = nameof(CanTestConnection))]
private async Task TestConnectionAsync()
{
    IsTestingConnection = true;
    TestResult = ConnectionTestResult.None;

    try
    {
        var result = await _connectionService.TestConnectionAsync(ServerUrl, ApiKey);
        TestResult = result.Status;
        TestResultMessage = result.Message;
        IsConnectionValid = result.Status == ConnectionTestResult.Success;
    }
    finally
    {
        IsTestingConnection = false;
    }
}

private bool CanTestConnection() =>
    !IsTestingConnection &&
    !string.IsNullOrWhiteSpace(ServerUrl) &&
    !string.IsNullOrWhiteSpace(ApiKey);
```

**Error classification logic:**
```csharp
public async Task<ConnectionTestOutcome> TestConnectionAsync(string serverUrl, string apiKey)
{
    var client = _httpClientFactory.CreateClient("SmartLogApi");
    var request = new HttpRequestMessage(HttpMethod.Get, $"{serverUrl.TrimEnd('/')}/api/v1/health/details");
    request.Headers.Add("X-API-Key", apiKey);

    try
    {
        var response = await client.SendAsync(request);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => new(ConnectionTestResult.Success, "Connection successful"),
            HttpStatusCode.Unauthorized => new(ConnectionTestResult.AuthError,
                "Invalid API key. Please verify your API key from the admin panel."),
            _ => new(ConnectionTestResult.UnexpectedError,
                $"Server returned unexpected status: {(int)response.StatusCode}")
        };
    }
    catch (TaskCanceledException) => // Timeout
    catch (HttpRequestException ex) => // Classify inner exception
    catch (BrokenCircuitException) => // Circuit breaker open
}
```

**Exception classification hierarchy:**
1. `TaskCanceledException` (not user-cancelled) -> Timeout
2. `HttpRequestException` with inner `SocketException` (ConnectionRefused) -> Connection Refused
3. `HttpRequestException` with inner `SocketException` (HostNotFound) -> DNS Failure
4. `HttpRequestException` with inner `AuthenticationException` -> TLS Error
5. `BrokenCircuitException` -> Connection Refused (server known to be down)
6. Any other exception -> Unexpected Error

### API Contracts

**GET /api/v1/health/details**

Request:
```
GET /api/v1/health/details HTTP/1.1
Host: 192.168.1.100:8443
X-API-Key: sk-device-001-abc123
```

Success Response (200):
```json
{
  "status": "healthy",
  "database": { "status": "healthy", "latencyMs": 5 },
  "uptime": "2d 5h 30m",
  "activeScanners": 3,
  "scansToday": 1250
}
```

Error Response (401):
```json
{
  "error": "InvalidApiKey",
  "message": "Invalid or missing API key"
}
```

### Data Requirements

**ConnectionTestResult enum:**

| Value | Description |
|-------|-------------|
| None | No test has been performed |
| Success | HTTP 200 received from /api/v1/health/details |
| AuthError | HTTP 401 received |
| ConnectionRefused | SocketException with ConnectionRefused |
| Timeout | TaskCanceledException (request exceeded timeout) |
| DnsFailure | SocketException with HostNotFound |
| TlsError | AuthenticationException during TLS handshake |
| UnexpectedError | Any other failure (includes unexpected HTTP status codes) |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Double-click Test Connection button | Button is disabled during test (IsTestingConnection=true); second click ignored; only one request sent |
| Test during network transition (WiFi switching, cable unplugged) | Request may fail with connection error; appropriate error message displayed; user can retry when network stabilizes |
| Server returns unexpected status code (e.g., 500 Internal Server Error) | Red X icon with message "Server returned unexpected status: 500. The server may be experiencing issues." |
| Malformed response body (server returns 200 but invalid JSON) | Treated as success since the status code is 200; response body is not parsed for connection validation purposes |
| Test with empty Server URL field | Test Connection button is disabled (CanTestConnection returns false); no request sent |
| Test with empty API Key field | Test Connection button is disabled (CanTestConnection returns false); no request sent |
| Server returns HTTP 500 (Internal Server Error) | Red X icon with message "Server returned unexpected status: 500. The server may be experiencing issues." |
| Slow response just under 10-second timeout (e.g., 9.5 seconds) | Request succeeds; result displayed normally; loading state clears |
| Server returns HTTP 301/302 redirect | HttpClient follows redirect by default; if redirected endpoint responds 200, treated as success; if redirect leads to error, classified accordingly |
| Connection test while previous test is in progress (edge case: UI race) | RelayCommand CanExecute prevents this; button disabled while IsTestingConnection=true |
| Circuit breaker is already open from prior failures (e.g., from Polly state in another part of app) | BrokenCircuitException caught; displayed as "Cannot reach server. Check the server URL and ensure the server is running." |
| Server URL has trailing slash: "https://server.local:8443/" | Trailing slash trimmed before appending /api/v1/health/details to avoid double slash |
| Test Connection succeeds, then user changes API Key field | Test result clears; Save button reverts to requiring a new successful test |
| Test Connection succeeds, then user changes Server URL field | Test result clears; Save button reverts to requiring a new successful test |
| Server returns HTTP 403 Forbidden | Red X icon with message "Server returned unexpected status: 403. Access denied." |
| Network returns ICMP unreachable (e.g., firewall blocking port) | HttpRequestException caught; mapped to "Cannot reach server. Check the server URL and ensure the server is running." |

---

## Test Scenarios

- [ ] Test Connection button is present on SetupPage between input fields and Save button
- [ ] Test Connection button is disabled when Server URL is empty
- [ ] Test Connection button is disabled when API Key is empty
- [ ] Test Connection button is enabled when both Server URL and API Key are non-empty
- [ ] Successful test (mock returns 200): displays green checkmark and "Connection successful"
- [ ] Successful test enables the Save button
- [ ] Auth error (mock returns 401): displays red X and "Invalid API key. Please verify your API key from the admin panel."
- [ ] Connection refused (mock throws HttpRequestException with SocketException ConnectionRefused): displays "Cannot reach server. Check the server URL and ensure the server is running."
- [ ] Timeout (mock throws TaskCanceledException): displays "Connection timed out. Check network connectivity."
- [ ] DNS failure (mock throws HttpRequestException with SocketException HostNotFound): displays "Server not found. Check the URL format."
- [ ] TLS error (mock throws HttpRequestException with AuthenticationException): displays "TLS certificate error. Enable self-signed certificate support."
- [ ] Unexpected status (mock returns 500): displays "Server returned unexpected status: 500"
- [ ] Loading state: button shows "Testing..." and is disabled during request
- [ ] Loading state clears after test completes (success or failure)
- [ ] Test result persists on screen after test completes
- [ ] Test result clears when Server URL field is modified
- [ ] Test result clears when API Key field is modified
- [ ] Test result does NOT clear when HMAC Secret, Scan Mode, or Scan Type fields are modified
- [ ] Double-click prevention: button disabled during test, only one HTTP request sent
- [ ] GET request sent to correct URL: {serverUrl}/api/v1/health/details
- [ ] GET request includes X-API-Key header with entered API key value
- [ ] Save button is disabled until a successful connection test
- [ ] BrokenCircuitException caught and displayed as connection refused message
- [ ] Trailing slash in server URL handled correctly (no double slash in request URL)
- [ ] Server URL with port number (e.g., https://10.0.0.5:8443) works correctly
- [ ] HTTP (non-HTTPS) server URL works correctly for local dev scenarios
- [ ] Polly retry policy applies to test connection request (mock returns 500, then 200; verify success after retry)
- [ ] Connection test logs result via Serilog (success logged at Information, failures at Warning)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0001 | Hard | ISecureConfigService and IPreferencesService for credential context (though test uses entered values, not stored values) | Draft |
| US0002 | Hard | Named HttpClient "SmartLogApi" via IHttpClientFactory with TLS configuration and Polly policies | Draft |
| US0004 | Hard | SetupPage and SetupViewModel to add the Test Connection button and logic to | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| SmartLog Admin Web App server | API | Must expose GET /api/v1/health/details with X-API-Key authentication |
| IHttpClientFactory ("SmartLogApi") | Infrastructure (US0002) | Configured with TLS and Polly policies |
| Network connectivity to server | Environment | Required for connection test to succeed |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

---

## Open Questions

- [ ] Should the connection test bypass Polly retry policies for faster feedback (fail fast), or use the configured retries (more resilient but slower feedback)? - Owner: Architect
- [ ] Should a successful connection test be required before the Save button is enabled, or should Save be always available with the test being optional? - Owner: Product
- [ ] Should the connection test response body (/api/v1/health/details) be displayed to IT Admin Ian (e.g., server uptime, active scanners), or just the pass/fail result? - Owner: Product
- [ ] Should the HMAC Secret field be included in the connection test validation (e.g., a test HMAC computation), or is API key validation sufficient for setup? - Owner: Architect

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
