# PL0005: Setup Connection Validation - Implementation Plan

> **Status:** Completed
> **Story:** [US0005: Implement Setup Connection Validation](../stories/US0005-setup-connection-validation.md)
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Created:** 2026-02-14
> **Language:** C# + XAML

## Overview

This plan adds "Test Connection" functionality to the SetupPage. IT Admin Ian can validate server connectivity and API key correctness before saving configuration. The test sends GET /api/v1/health/details with X-API-Key header. Different failure modes (401, connection refused, timeout, DNS failure, TLS error) produce specific, actionable error messages.

The implementation uses **TDD** approach because:
1. HTTP failure mode detection has well-defined scenarios
2. Service can be tested in isolation with mock HttpMessageHandler
3. Error message mapping logic is pure and testable
4. ViewModel logic has clear success/failure paths

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Test Connection button present | Secondary/outlined button below API Key field |
| AC2 | Successful connection test (200) | Green checkmark + "Connection successful" message |
| AC3 | Authentication error (401) | Red X + "Invalid API key" message |
| AC4 | Connection refused | Red X + "Cannot reach server" message |
| AC5 | Connection timeout | Red X + "Connection timed out" message |
| AC6 | DNS resolution failure | Red X + "Server not found" message |
| AC7 | TLS certificate error | Red X + "TLS certificate error" message |
| AC8 | Loading state during test | Button shows "Testing..." with loading indicator |
| AC9 | Test results persist | Result remains visible until next test or field change |
| AC10 | Test results clear on field change | Result cleared when URL or API Key modified |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12
- **Framework:** .NET 8.0 MAUI (MacCatalyst + Windows)
- **Test Framework:** xUnit 2.6.4 + Moq 4.20.70
- **HTTP Client:** Named HttpClient "SmartLogApi" with Polly policies

### Relevant Best Practices
- Mock HttpMessageHandler for unit testing HTTP calls
- Enum for connection test results (strongly typed status)
- Property change notifications clear test results when URL/Key changes
- CanExecute on RelayCommand to enable/disable Test Connection button

### Library Documentation (Context7)

| Library | Key Patterns |
|---------|--------------|
| HttpClient | `client.SendAsync(request)`, catch `HttpRequestException`, `TaskCanceledException`, check `StatusCode` |
| Moq HttpMessageHandler | `Mock<HttpMessageHandler>().Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ...)` |
| CommunityToolkit.Mvvm | `[RelayCommand(CanExecute = nameof(MethodName))]`, `NotifyCanExecuteChanged()` on property changes |

### Existing Patterns
- **Named HttpClient**: "SmartLogApi" already configured in MauiProgram.cs with Polly policies
- **SetupViewModel**: Already exists with validation and save logic
- **SetupPage.xaml**: Already has form UI, need to add Test Connection button

---

## Recommended Approach

**Strategy:** TDD (Test-Driven Development)
**Rationale:**
- Connection service has well-defined HTTP failure scenarios
- Mock HttpMessageHandler enables isolated testing without real server
- Error mapping logic is pure (HttpException → ConnectionTestResult enum)
- ViewModel logic has clear test cases (button enabled, result display)

### Test Priority
1. **Service tests**: All 7 HTTP failure modes (200, 401, connection refused, timeout, DNS, TLS, unexpected)
2. **ViewModel tests**: TestConnectionCommand execution, loading state, result clearing

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Create ConnectionTestResult enum | `Core/Models/ConnectionTestResult.cs` | None | [ ] |
| 2 | Create IConnectionTestService interface | `Core/Services/IConnectionTestService.cs` | Task 1 | [ ] |
| 3 | Write ConnectionTestService tests (7 failure modes) | `Tests/Services/ConnectionTestServiceTests.cs` | Task 2 | [ ] |
| 4 | Implement ConnectionTestService | `Core/Services/ConnectionTestService.cs` | Task 3 | [ ] |
| 5 | Write SetupViewModel TestConnection tests | `Tests/ViewModels/SetupViewModelTests.cs` | Task 4 | [ ] |
| 6 | Add TestConnection logic to SetupViewModel | `Core/ViewModels/SetupViewModel.cs` | Task 5 | [ ] |
| 7 | Register IConnectionTestService in DI | `MauiProgram.cs` | Task 6 | [ ] |
| 8 | Add Test Connection button to SetupPage UI | `Views/SetupPage.xaml` | Task 7 | [ ] |
| 9 | Manual testing: Test all failure modes | Manual | Task 8 | [ ] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| Group 1 | Tasks 1-2 | None (create contracts) |
| Group 2 | Tasks 3-4 | Group 1 (TDD: test → implement) |
| Group 3 | Tasks 5-6 | Group 2 (TDD: test → implement) |
| Group 4 | Tasks 7-8 | Group 3 (register + UI) |
| Group 5 | Task 9 | Group 4 (manual verification) |

---

## Implementation Phases

### Phase 1: Create Contracts (Tasks 1-2)
**Goal:** Define ConnectionTestResult enum and service interface

- [ ] Create `Core/Models/ConnectionTestResult.cs`:
  ```csharp
  namespace SmartLog.Scanner.Core.Models;

  /// <summary>
  /// US0005: Result status of connection test to SmartLog API server.
  /// Maps HTTP failure modes to user-friendly error messages.
  /// </summary>
  public enum ConnectionTestResult
  {
      /// <summary>No test has been run yet</summary>
      None,

      /// <summary>HTTP 200: Connection successful, API key valid</summary>
      Success,

      /// <summary>HTTP 401: Invalid API key</summary>
      AuthError,

      /// <summary>SocketException: Connection refused (server not listening)</summary>
      ConnectionRefused,

      /// <summary>TaskCanceledException: Request timed out (10 seconds)</summary>
      Timeout,

      /// <summary>SocketException: DNS name resolution failure</summary>
      DnsFailure,

      /// <summary>AuthenticationException: TLS certificate error</summary>
      TlsError,

      /// <summary>Unexpected HTTP error (5xx, network error, etc.)</summary>
      UnexpectedError
  }

  /// <summary>
  /// US0005: Result of a connection test including status and user-facing message.
  /// </summary>
  public record ConnectionTestResultDto(
      ConnectionTestResult Status,
      string Message,
      string? Details = null
  );
  ```

- [ ] Create `Core/Services/IConnectionTestService.cs`:
  ```csharp
  namespace SmartLog.Scanner.Core.Services;

  /// <summary>
  /// US0005: Service for testing connectivity to SmartLog API server.
  /// Validates server URL and API key by sending GET /api/v1/health/details.
  /// </summary>
  public interface IConnectionTestService
  {
      /// <summary>
      /// Tests connection to the SmartLog API server.
      /// </summary>
      /// <param name="serverUrl">Base URL (e.g., "https://192.168.1.100:8443")</param>
      /// <param name="apiKey">API key to test (sent in X-API-Key header)</param>
      /// <returns>Result with status and user-facing message</returns>
      Task<ConnectionTestResultDto> TestConnectionAsync(string serverUrl, string apiKey);
  }
  ```

**Files:**
- `SmartLog.Scanner.Core/Models/ConnectionTestResult.cs`
- `SmartLog.Scanner.Core/Services/IConnectionTestService.cs`

### Phase 2: TDD - ConnectionTestService (Tasks 3-4)
**Goal:** Write failing tests for all 7 failure modes, then implement

**Test cases (ConnectionTestServiceTests.cs):**
- [ ] TestConnectionAsync_Http200_ReturnsSuccess
- [ ] TestConnectionAsync_Http401_ReturnsAuthError
- [ ] TestConnectionAsync_ConnectionRefused_ReturnsConnectionRefused
- [ ] TestConnectionAsync_Timeout_ReturnsTimeout
- [ ] TestConnectionAsync_DnsFailure_ReturnsDnsFailure
- [ ] TestConnectionAsync_TlsError_ReturnsTlsError
- [ ] TestConnectionAsync_Http500_ReturnsUnexpectedError
- [ ] TestConnectionAsync_InvalidUrl_ThrowsArgumentException
- [ ] TestConnectionAsync_EmptyApiKey_ThrowsArgumentException

**Implementation (ConnectionTestService.cs):**
```csharp
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

public class ConnectionTestService : IConnectionTestService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ConnectionTestService> _logger;

    public ConnectionTestService(
        IHttpClientFactory httpClientFactory,
        ILogger<ConnectionTestService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ConnectionTestResultDto> TestConnectionAsync(string serverUrl, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new ArgumentException("Server URL cannot be empty", nameof(serverUrl));

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));

        try
        {
            // Build request to GET /api/v1/health/details with X-API-Key header
            var client = _httpClientFactory.CreateClient("SmartLogApi");
            var requestUri = new Uri(new Uri(serverUrl), "/api/v1/health/details");

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Add("X-API-Key", apiKey);

            var response = await client.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return new ConnectionTestResultDto(
                    ConnectionTestResult.Success,
                    "Connection successful");
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ConnectionTestResultDto(
                    ConnectionTestResult.AuthError,
                    "Invalid API key. Please verify your API key from the admin panel.");
            }
            else
            {
                return new ConnectionTestResultDto(
                    ConnectionTestResult.UnexpectedError,
                    $"Server returned {(int)response.StatusCode}. Check server logs.",
                    response.StatusCode.ToString());
            }
        }
        catch (TaskCanceledException)
        {
            return new ConnectionTestResultDto(
                ConnectionTestResult.Timeout,
                "Connection timed out. Check network connectivity.");
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException socketEx)
        {
            if (socketEx.SocketErrorCode == SocketError.ConnectionRefused)
            {
                return new ConnectionTestResultDto(
                    ConnectionTestResult.ConnectionRefused,
                    "Cannot reach server. Check the server URL and ensure the server is running.");
            }
            else if (socketEx.SocketErrorCode == SocketError.HostNotFound ||
                     socketEx.SocketErrorCode == SocketError.TryAgain ||
                     socketEx.SocketErrorCode == SocketError.NoData)
            {
                return new ConnectionTestResultDto(
                    ConnectionTestResult.DnsFailure,
                    "Server not found. Check the URL format.");
            }
            else
            {
                return new ConnectionTestResultDto(
                    ConnectionTestResult.UnexpectedError,
                    $"Network error: {socketEx.SocketErrorCode}",
                    socketEx.Message);
            }
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            return new ConnectionTestResultDto(
                ConnectionTestResult.TlsError,
                "TLS certificate error. Enable self-signed certificate support.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during connection test");
            return new ConnectionTestResultDto(
                ConnectionTestResult.UnexpectedError,
                "Unexpected error occurred. Check logs.",
                ex.Message);
        }
    }
}
```

**Files:**
- `SmartLog.Scanner.Tests/Services/ConnectionTestServiceTests.cs` - 9 tests
- `SmartLog.Scanner.Core/Services/ConnectionTestService.cs` - Implementation

### Phase 3: TDD - SetupViewModel TestConnection (Tasks 5-6)
**Goal:** Add TestConnection command to ViewModel with tests

**Test cases (add to SetupViewModelTests.cs):**
- [ ] TestConnectionAsync_SuccessResult_SetsIsConnectionValidTrue
- [ ] TestConnectionAsync_SuccessResult_DisplaysSuccessMessage
- [ ] TestConnectionAsync_AuthErrorResult_SetsIsConnectionValidFalse
- [ ] TestConnectionAsync_AuthErrorResult_DisplaysAuthErrorMessage
- [ ] TestConnectionAsync_SetsIsTestingConnectionTrue_DuringTest_ThenFalse
- [ ] TestConnectionAsync_EmptyUrl_CommandCannotExecute
- [ ] TestConnectionAsync_EmptyApiKey_CommandCannotExecute
- [ ] OnServerUrlChanged_ClearsTestResult
- [ ] OnApiKeyChanged_ClearsTestResult

**Implementation (add to SetupViewModel.cs):**
```csharp
// Add to SetupViewModel class

private readonly IConnectionTestService _connectionTestService;

// Constructor: add IConnectionTestService parameter

// New properties
[ObservableProperty] private bool _isTestingConnection;
[ObservableProperty] private ConnectionTestResult _testResult = ConnectionTestResult.None;
[ObservableProperty] private string? _testResultMessage;
[ObservableProperty] private bool _isConnectionValid;

// New command
[RelayCommand(CanExecute = nameof(CanTestConnection))]
private async Task TestConnectionAsync()
{
    IsTestingConnection = true;
    TestResult = ConnectionTestResult.None;
    TestResultMessage = null;

    try
    {
        var result = await _connectionTestService.TestConnectionAsync(ServerUrl, ApiKey);
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

// Update existing properties to clear test results on change
partial void OnServerUrlChanged(string value)
{
    TestResult = ConnectionTestResult.None;
    TestResultMessage = null;
    IsConnectionValid = false;
    TestConnectionCommand.NotifyCanExecuteChanged();
}

partial void OnApiKeyChanged(string value)
{
    TestResult = ConnectionTestResult.None;
    TestResultMessage = null;
    IsConnectionValid = false;
    TestConnectionCommand.NotifyCanExecuteChanged();
}
```

**Files:**
- `SmartLog.Scanner.Tests/ViewModels/SetupViewModelTests.cs` - Add 9 tests
- `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs` - Add TestConnection logic

### Phase 4: DI Registration & UI (Tasks 7-8)
**Goal:** Register service and add Test Connection button

- [ ] Update `MauiProgram.cs`:
  ```csharp
  // US0005: Register connection test service
  builder.Services.AddSingleton<IConnectionTestService, ConnectionTestService>();
  ```

- [ ] Update `Views/SetupPage.xaml` (add after HMAC Secret field, before Save button):
  ```xml
  <!-- Test Connection Button (AC1) -->
  <Button Text="{Binding IsTestingConnection, Converter={StaticResource TestingTextConverter}}"
          Command="{Binding TestConnectionCommand}"
          IsEnabled="{Binding IsTestingConnection, Converter={StaticResource InvertedBoolConverter}}"
          StyleClass="SecondaryButton"
          Margin="0,10,0,0">
      <Button.ImageSource>
          <FontImageSource Glyph="&#xE943;"
                           FontFamily="SegoeMDL2Assets"
                           Color="{StaticResource Primary}" />
      </Button.ImageSource>
  </Button>

  <!-- Test Result Display (AC2-AC7, AC9) -->
  <HorizontalStackLayout IsVisible="{Binding TestResult, Converter={StaticResource EnumNotNoneConverter}}"
                         Spacing="10"
                         Margin="0,10,0,0">
      <Label Text="{Binding TestResult, Converter={StaticResource TestResultIconConverter}}"
             FontSize="20" />
      <Label Text="{Binding TestResultMessage}"
             TextColor="{Binding TestResult, Converter={StaticResource TestResultColorConverter}}"
             VerticalOptions="Center" />
  </HorizontalStackLayout>

  <!-- ActivityIndicator (AC8) -->
  <ActivityIndicator IsRunning="{Binding IsTestingConnection}"
                     IsVisible="{Binding IsTestingConnection}"
                     Color="{StaticResource Primary}"
                     Margin="0,10,0,0" />
  ```

**Note:** Need to create additional converters:
- `TestingTextConverter`: "Test Connection" → "Testing..." when IsTestingConnection=true
- `EnumNotNoneConverter`: ConnectionTestResult.None → false, else → true
- `TestResultIconConverter`: Success → "✓", errors → "✗"
- `TestResultColorConverter`: Success → Green, errors → Red

**Files:**
- `MauiProgram.cs` - Register IConnectionTestService
- `Views/SetupPage.xaml` - Add Test Connection button and result display
- `Converters/TestingTextConverter.cs`, `EnumNotNoneConverter.cs`, etc.

### Phase 5: Testing & Validation
**Goal:** Verify all acceptance criteria

| AC | Verification Method | File Evidence | Status |
|----|---------------------|---------------|--------|
| AC1 | Manual: Button visible on SetupPage | SetupPage.xaml | Pending |
| AC2 | Unit test: Success result | ConnectionTestServiceTests | Pending |
| AC3 | Unit test: 401 → AuthError | ConnectionTestServiceTests | Pending |
| AC4 | Unit test: ConnectionRefused | ConnectionTestServiceTests | Pending |
| AC5 | Unit test: Timeout | ConnectionTestServiceTests | Pending |
| AC6 | Unit test: DNS failure | ConnectionTestServiceTests | Pending |
| AC7 | Unit test: TLS error | ConnectionTestServiceTests | Pending |
| AC8 | Unit test: IsTestingConnection state | SetupViewModelTests | Pending |
| AC9 | Manual: Result persists | UI inspection | Pending |
| AC10 | Unit test: Field change clears result | SetupViewModelTests | Pending |

---

## Edge Case Handling

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | Malformed server URL | ValidationAll() already catches this before Test Connection | Phase 1 |
| 2 | Test Connection pressed repeatedly | CanTestConnection() returns false when IsTestingConnection=true | Phase 3 |
| 3 | Polly retry delays connection test | Polly policies apply automatically (configured in US0002) | Phase 2 |
| 4 | Mixed success/failure across retries | Final response determines result (Polly handles retries transparently) | Phase 2 |
| 5 | Very slow server (9.9 seconds) | Returns success if response arrives before 10-second timeout | Phase 2 |
| 6 | HTTP 403, 404, 5xx errors | Mapped to UnexpectedError with status code in message | Phase 2 |
| 7 | User modifies URL during test | OnServerUrlChanged clears result; new test can be triggered | Phase 3 |
| 8 | Connection succeeds but Save fails | Test result persists; user sees green checkmark and red save error separately | Phase 3 |

**Coverage:** 8/8 edge cases handled

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| HttpMessageHandler mocking complexity | Tests fragile or hard to maintain | Use well-documented Moq patterns; one mock setup per test |
| SocketErrorCode platform differences | DNS failure detection fails on some platforms | Test both HostNotFound and TryAgain/NoData error codes |
| Polly retry masking failure modes | 401 might retry and succeed incorrectly | Polly only retries transient errors (5xx, timeouts); 401 fails immediately |
| Real server required for manual testing | Cannot verify all failure modes | Use mock server or Postman mock for 401/timeout scenarios |
| UI converters not tested | Converters break silently | Keep converters simple; verify manually during Phase 4 |

---

## Definition of Done

- [ ] All acceptance criteria implemented (AC1-AC10)
- [ ] ConnectionTestService with 9 unit tests (7 failure modes + 2 validation)
- [ ] SetupViewModel with 9 additional tests (command + state management)
- [ ] Test Connection button in SetupPage.xaml
- [ ] IConnectionTestService registered in DI
- [ ] Manual tests pass:
  - Button enabled when URL and API key filled
  - Success message shows green checkmark
  - Failure message shows red X
  - Loading state shows during test
  - Result clears when URL or API Key changed
- [ ] All tests pass (existing + new)
- [ ] Build successful

---

## Notes

**Why TDD:**
ConnectionTestService has well-defined HTTP failure scenarios that can be tested in isolation using mock HttpMessageHandler. This ensures all error paths are covered before implementation.

**Converters:**
Need 4 new value converters for UI. These are simple and don't require unit tests, but should be verified manually.

**Polly Integration:**
The named HttpClient "SmartLogApi" already has Polly retry and circuit breaker policies from US0002. These apply automatically to connection test requests. Importantly:
- Polly retries 5xx errors and timeouts (transient failures)
- Polly does NOT retry 401 (permanent failure)
- Connection refused and DNS failures throw before Polly can intervene

**Future Enhancement:**
US0005 is setup-only. Connection testing from Settings page is out of scope for v1.0 but could reuse the same IConnectionTestService in a future story.
