# PL0002: Self-Signed TLS and HTTP Client Infrastructure - Implementation Plan

> **Status:** Completed
> **Story:** [US0002: Configure Self-Signed TLS and HTTP Client Infrastructure](../stories/US0002-self-signed-tls-and-http-client-infrastructure.md)
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Created:** 2026-02-14
> **Language:** C#

## Overview

This plan implements resilient HTTP client infrastructure for communicating with the SmartLog Admin Web App server. It configures a named HttpClient "SmartLogApi" via IHttpClientFactory with self-signed certificate acceptance (configurable), Polly retry policy (3 retries, exponential backoff), and circuit breaker (5 failures, 30s break). Configuration is loaded from appsettings.json (already exists in Resources/Raw/) at startup in MauiProgram.cs.

The implementation uses **Test-After** approach because:
1. Configuration code in MauiProgram.cs is hard to test in isolation (requires MAUI app context)
2. Polly policies are library code (already tested by Microsoft/Polly teams)
3. Integration tests would require actual HTTP server or complex mocking
4. Code is straightforward configuration following established patterns

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Named HttpClient registration | IHttpClientFactory.CreateClient("SmartLogApi") returns configured HttpClient |
| AC2 | Self-signed cert acceptance (default) | AcceptSelfSignedCerts=true enables ServerCertificateCustomValidationCallback |
| AC3 | Self-signed cert rejection | AcceptSelfSignedCerts=false uses default cert validation |
| AC4 | Warning logged | Serilog warning when AcceptSelfSignedCerts=true |
| AC5 | Polly retry policy | 3 retries with 1s, 2s, 4s backoff; logs each retry |
| AC6 | Polly circuit breaker | Opens after 5 failures for 30s; logs state changes |
| AC7 | Configurable timeout | HttpClient.Timeout from Server.TimeoutSeconds config |
| AC8 | Cross-platform | Works on macOS and Windows without platform-specific code |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12
- **Framework:** .NET 8.0 MAUI (MacCatalyst + Windows)
- **Test Framework:** xUnit 2.6.4 + Moq 4.20.70 (for unit tests where applicable)

### Relevant Best Practices
- Named HttpClient pattern via IHttpClientFactory (avoids socket exhaustion)
- Polly policy chaining: retry → circuit breaker (retry wraps CB)
- Configuration loading from embedded resources (appsettings.json as MauiAsset)
- Serilog structured logging for policy events
- Dangerous cert callback only when explicitly configured

### Library Documentation (Context7)

| Library | Key Patterns |
|---------|--------------|
| IHttpClientFactory | `services.AddHttpClient("name")` registers named client; `factory.CreateClient("name")` retrieves it |
| Polly 8.x | `AddTransientHttpErrorPolicy()` extension for retry/CB; `WaitAndRetryAsync(count, sleepDurationProvider)`, `CircuitBreakerAsync(failureThreshold, durationOfBreak)` |
| Microsoft.Extensions.Configuration | `ConfigurationBuilder().AddJsonStream(stream).Build()` loads appsettings.json; `config.GetValue<T>(key, default)` reads values |
| HttpClientHandler | `ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator` accepts all certs |

### Existing Patterns
- **MauiProgram.cs**: Already configures Serilog, registers US0001 services
- **appsettings.json**: Already exists at `Resources/Raw/appsettings.json` with Server.TimeoutSeconds and Server.AcceptSelfSignedCerts
- **Logging**: Serilog already configured with file and console sinks

---

## Recommended Approach

**Strategy:** Test-After (Code First)
**Rationale:**
- MauiProgram.cs configuration is hard to unit test (requires MAUI app host)
- Polly policies are library code with their own test coverage
- HttpClient factory behavior is framework code
- Configuration loading is straightforward Microsoft.Extensions.Configuration usage
- Code review + manual integration testing more appropriate than complex mocking

### Test Priority
1. **Manual integration test**: Build and run app, verify HttpClient creation succeeds
2. **Configuration unit test**: Verify appsettings.json values are loaded correctly (can test ConfigurationBuilder in isolation)
3. **Mock HttpMessageHandler test** (optional): Test Polly policies with mock HTTP responses

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Verify appsettings.json has required keys | `Resources/Raw/appsettings.json` | None | [ ] |
| 2 | Add NuGet packages (Polly, Configuration.Json) | `SmartLog.Scanner.csproj` | None | [ ] |
| 3 | Load configuration from appsettings.json | `MauiProgram.cs` | Task 2 | [ ] |
| 4 | Register named HttpClient "SmartLogApi" | `MauiProgram.cs` | Task 3 | [ ] |
| 5 | Configure self-signed cert acceptance | `MauiProgram.cs` | Task 4 | [ ] |
| 6 | Add warning log for self-signed certs | `MauiProgram.cs` | Task 5 | [ ] |
| 7 | Configure HTTP timeout from config | `MauiProgram.cs` | Task 4 | [ ] |
| 8 | Add Polly retry policy | `MauiProgram.cs` | Task 4 | [ ] |
| 9 | Add Polly circuit breaker policy | `MauiProgram.cs` | Task 8 | [ ] |
| 10 | (Optional) Create integration test | `Tests/Infrastructure/HttpClientTests.cs` | Task 9 | [ ] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| Group 1 | Tasks 1, 2 | None (can run in parallel) |
| Group 2 | Tasks 3-9 | Group 1 complete (sequential in MauiProgram.cs) |
| Group 3 | Task 10 | Group 2 complete (optional) |

---

## Implementation Phases

### Phase 1: Prerequisites (Tasks 1-2)
**Goal:** Ensure appsettings.json and NuGet packages are ready

- [ ] Verify `Resources/Raw/appsettings.json` contains:
  ```json
  {
    "Server": {
      "TimeoutSeconds": 10,
      "AcceptSelfSignedCerts": true
    }
  }
  ```
  (Already exists from project setup)

- [ ] Add NuGet packages to `SmartLog.Scanner.csproj`:
  - `Microsoft.Extensions.Http.Polly` (version 8.0.0)
  - Verify `Microsoft.Extensions.Configuration.Json` already added (version 8.0.0)

**Files:**
- `SmartLog.Scanner/SmartLog.Scanner.csproj` - Add package references
- `SmartLog.Scanner/Resources/Raw/appsettings.json` - Verify (already exists)

### Phase 2: Configuration Loading (Task 3)
**Goal:** Load appsettings.json into IConfiguration

- [ ] In `MauiProgram.cs`, after `var builder = MauiApp.CreateBuilder();`:
  ```csharp
  // Load appsettings.json configuration
  var appSettingsStream = await FileSystem.OpenAppPackageFileAsync("appsettings.json");
  var config = new ConfigurationBuilder()
      .AddJsonStream(appSettingsStream)
      .Build();
  builder.Configuration.AddConfiguration(config);
  ```

- [ ] Read configuration values:
  ```csharp
  var acceptSelfSigned = config.GetValue<bool>("Server:AcceptSelfSignedCerts", true);
  var timeoutSeconds = config.GetValue<int>("Server:TimeoutSeconds", 10);
  ```

**Files:**
- `MauiProgram.cs` - Add configuration loading (lines ~15-25)

### Phase 3: Named HttpClient Registration (Task 4)
**Goal:** Register "SmartLogApi" HttpClient via IHttpClientFactory

- [ ] Add HttpClient registration with base configuration:
  ```csharp
  builder.Services.AddHttpClient("SmartLogApi")
      .ConfigureHttpClient(client =>
      {
          client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
      });
  ```

**Files:**
- `MauiProgram.cs` - Add HttpClient registration

### Phase 4: Self-Signed Certificate Acceptance (Tasks 5-6)
**Goal:** Configure TLS callback and logging

- [ ] Configure HttpClientHandler with certificate callback:
  ```csharp
  builder.Services.AddHttpClient("SmartLogApi")
      .ConfigurePrimaryHttpMessageHandler(() =>
      {
          var handler = new HttpClientHandler();
          if (acceptSelfSigned)
          {
              handler.ServerCertificateCustomValidationCallback =
                  HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
          }
          return handler;
      })
      .ConfigureHttpClient(client =>
      {
          client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
      });
  ```

- [ ] Add warning log before HttpClient registration:
  ```csharp
  if (acceptSelfSigned)
  {
      Log.Warning("Self-signed TLS certificate acceptance is enabled. " +
          "This reduces TLS security and should only be used on trusted LANs.");
  }
  ```

**Files:**
- `MauiProgram.cs` - Update HttpClient registration with handler, add logging

### Phase 5: Polly Retry Policy (Task 8)
**Goal:** Add exponential backoff retry policy

- [ ] Chain retry policy to HttpClient:
  ```csharp
  .AddTransientHttpErrorPolicy(policy =>
      policy.WaitAndRetryAsync(
          retryCount: 3,
          sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1)),
          onRetry: (outcome, timespan, retryCount, context) =>
          {
              Log.Warning("HTTP request retry {RetryCount} after {Delay}s due to {Exception}",
                  retryCount, timespan.TotalSeconds, outcome.Exception?.GetType().Name ?? "transient error");
          }))
  ```

**Files:**
- `MauiProgram.cs` - Add retry policy with logging

### Phase 6: Polly Circuit Breaker Policy (Task 9)
**Goal:** Add circuit breaker after retry policy

- [ ] Chain circuit breaker policy (after retry):
  ```csharp
  .AddTransientHttpErrorPolicy(policy =>
      policy.CircuitBreakerAsync(
          handledEventsAllowedBeforeBreaking: 5,
          durationOfBreak: TimeSpan.FromSeconds(30),
          onBreak: (outcome, duration) =>
          {
              Log.Error("HTTP circuit breaker opened for {Duration}s due to {Exception}",
                  duration.TotalSeconds, outcome.Exception?.GetType().Name ?? "transient errors");
          },
          onReset: () =>
          {
              Log.Information("HTTP circuit breaker reset (closed)");
          },
          onHalfOpen: () =>
          {
              Log.Information("HTTP circuit breaker half-open (testing)");
          }));
  ```

**Files:**
- `MauiProgram.cs` - Add circuit breaker policy with logging

### Phase 7: Testing & Validation (Task 10)
**Goal:** Verify all acceptance criteria

| AC | Verification Method | File Evidence | Status |
|----|---------------------|---------------|--------|
| AC1 | Code review + manual test (inject IHttpClientFactory and call CreateClient("SmartLogApi")) | `MauiProgram.cs:AddHttpClient("SmartLogApi")` | Pending |
| AC2 | Code review (ServerCertificateCustomValidationCallback set when flag=true) | `MauiProgram.cs:DangerousAcceptAnyServerCertificateValidator` | Pending |
| AC3 | Code review (no callback set when flag=false; default validation used) | `MauiProgram.cs:if (acceptSelfSigned)` | Pending |
| AC4 | Code review + manual test (check logs at startup) | `MauiProgram.cs:Log.Warning` | Pending |
| AC5 | Code review (WaitAndRetryAsync with 3 retries, exponential backoff) | `MauiProgram.cs:Math.Pow(2, retryAttempt - 1)` | Pending |
| AC6 | Code review (CircuitBreakerAsync with 5 failures, 30s break) | `MauiProgram.cs:CircuitBreakerAsync(5, TimeSpan.FromSeconds(30))` | Pending |
| AC7 | Code review (client.Timeout from config) | `MauiProgram.cs:TimeSpan.FromSeconds(timeoutSeconds)` | Pending |
| AC8 | Manual test on macOS (builds and runs) | Build succeeds on MacCatalyst | Pending |

---

## Edge Case Handling

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | Invalid/malformed certificate | AcceptSelfSignedCerts=true accepts all; =false rejects | Phase 4 (Task 5) |
| 2 | Expired certificate | Same as #1; callback accepts all when true | Phase 4 (Task 5) |
| 3 | TLS handshake failure | HttpRequestException; Polly retries 3x with backoff | Phase 5 (Task 8) |
| 4 | Timeout during TLS handshake | TaskCanceledException after 10s; Polly retries | Phase 5 (Task 8) |
| 5 | Circuit breaker open | BrokenCircuitException thrown immediately; no HTTP call | Phase 6 (Task 9) |
| 6 | Retry exhaustion (3 failures) | Original exception propagates after final retry | Phase 5 (Task 8) |
| 7 | appsettings.json missing/unreadable | Default values used: AcceptSelfSignedCerts=true, TimeoutSeconds=10 | Phase 2 (Task 3) |
| 8 | AcceptSelfSignedCerts key missing | Defaults to true (GetValue second parameter) | Phase 2 (Task 3) |
| 9 | Server responds 503 (Service Unavailable) | Classified as transient error; Polly retries | Phase 5 (Task 8) |
| 10 | Success after 2 failed attempts | Third attempt succeeds; total delay 1s+2s=3s | Phase 5 (Task 8) |
| 11 | Circuit breaker half-open | First request tests circuit; success closes it, failure reopens | Phase 6 (Task 9) |
| 12 | HttpClient handler recycling | IHttpClientFactory manages lifetime; policies survive recycling | Phase 3 (Task 4) |

**Coverage:** 12/12 edge cases handled

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Polly 8.x API changes from previous versions | Code doesn't compile or policies don't work | Use Microsoft.Extensions.Http.Polly 8.0.0 which provides AddTransientHttpErrorPolicy extension |
| appsettings.json not loaded correctly at runtime | Config values default to true/10; app works but logs warning | Add defensive GetValue with defaults; test config loading manually |
| Retry and circuit breaker interaction unclear | Circuit breaker might trip too early or too late | Follow established pattern: retry wraps CB (retry policy added first, CB added second) |
| Cross-platform TLS callback behavior differs | macOS Keychain vs Windows DPAPI might handle certs differently | Use HttpClientHandler.DangerousAcceptAnyServerCertificateValidator (works on all platforms) |
| MAUI FileSystem.OpenAppPackageFileAsync fails on some platforms | appsettings.json not loaded; app crashes at startup | Wrap in try/catch; fall back to defaults if loading fails |

---

## Definition of Done

- [ ] All acceptance criteria implemented (AC1-AC8)
- [ ] appsettings.json contains required configuration keys
- [ ] Named HttpClient "SmartLogApi" registered in DI container
- [ ] Polly retry and circuit breaker policies configured
- [ ] Logging added for self-signed cert warning and policy events
- [ ] Code follows best practices (named client pattern, policy chaining)
- [ ] Manual test: app builds and starts without errors
- [ ] Manual test: logs show warning when AcceptSelfSignedCerts=true

---

## Notes

**Why Test-After:**
This is primarily configuration code in MauiProgram.cs. Testing would require:
1. Mocking IHttpClientFactory and HttpClient behavior (complex)
2. Creating mock HttpMessageHandler responses (Polly policy testing)
3. Running integration tests against actual HTTP endpoints
4. Or accepting that framework/library code (IHttpClientFactory, Polly) is already tested

For this story, code review + manual integration testing (verify app starts, logs appear, HttpClient creation succeeds) is more pragmatic than complex mocking infrastructure.

**Policy Chaining Order:**
Polly policies wrap each other in reverse order of registration:
```
Request → Retry → CircuitBreaker → HttpClientHandler → Server
```
This means retries happen before the circuit breaker counts failures, which is the desired behavior.

**Configuration Loading:**
appsettings.json is already bundled as a MauiAsset from project setup. FileSystem.OpenAppPackageFileAsync accesses it at runtime. IConfiguration is added to the DI container, making it injectable into services (useful for future stories).

**HttpClient Lifetime:**
IHttpClientFactory manages HttpMessageHandler lifetime (default 2 minutes). Polly policies and TLS callbacks are configured on the factory, not individual clients, so they persist across handler recycling.

**Future Enhancement:**
US0005 (Setup Connection Validation) will be the first story to actually use this HttpClient to make API calls. This story just sets up the infrastructure.
