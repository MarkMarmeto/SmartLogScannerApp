# US0002: Configure Self-Signed TLS and HTTP Client Infrastructure

> **Status:** Done
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As a** system (benefiting IT Admin Ian)
**I want** a resilient HTTP client infrastructure that accepts self-signed TLS certificates and includes automatic retry and circuit breaker policies
**So that** the scanner communicates reliably with the SmartLog server on school LANs that use self-signed certificates, and transient network failures are handled automatically without manual intervention

## Context

### Persona Reference
**IT Admin Ian** - School IT administrator, intermediate technical proficiency, deploys scanner devices on school LANs that commonly use self-signed TLS certificates. Needs the scanner to "just work" on the school network without requiring CA-issued certificates.
[Full persona details](../personas.md#it-admin-ian)

### Background
School LAN deployments commonly use self-signed TLS certificates for the SmartLog Admin Web App server. The scanner must connect over HTTPS without requiring a CA-issued certificate. Additionally, school LAN connectivity is variable -- network blips, server restarts, and transient failures are expected. This story configures the foundational HTTP infrastructure in MauiProgram.cs: a named HttpClient via IHttpClientFactory with self-signed certificate acceptance, Polly retry with exponential backoff, and a circuit breaker policy. All subsequent stories that communicate with the server (US0005 connection validation, EP0002 health checks, EP0003 scan submission, EP0004 background sync) depend on this infrastructure.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Security | Self-signed TLS acceptance controlled by configuration flag | AcceptSelfSignedCerts must be readable from appsettings.json, default true |
| PRD | Security | Warning logged when self-signed cert acceptance is active | Serilog must log a warning at startup when the flag is true |
| TRD | Architecture | Named HttpClient registered via IHttpClientFactory | Must use AddHttpClient("SmartLogApi") pattern in MauiProgram.cs |
| TRD | Resilience | Polly retry: 3 retries with exponential backoff | AddTransientHttpErrorPolicy with WaitAndRetryAsync |
| TRD | Resilience | Polly circuit breaker: 5 failures, 30s break | AddTransientHttpErrorPolicy with CircuitBreakerAsync |
| TRD | Performance | 10-second HTTP timeout (configurable) | HttpClient.Timeout = TimeSpan.FromSeconds(config value) |

---

## Acceptance Criteria

### AC1: Named HttpClient registered via IHttpClientFactory
- **Given** MauiProgram.cs configures the DI container
- **When** the application starts
- **Then** a named HttpClient "SmartLogApi" is registered via services.AddHttpClient("SmartLogApi") with IHttpClientFactory, resolvable by injecting IHttpClientFactory and calling CreateClient("SmartLogApi")

### AC2: Self-signed certificate acceptance enabled by default
- **Given** the appsettings.json contains "Server.AcceptSelfSignedCerts" set to true (or the key is absent, defaulting to true)
- **When** the "SmartLogApi" HttpClient makes an HTTPS request to a server with a self-signed certificate
- **Then** the TLS handshake succeeds because HttpClientHandler.ServerCertificateCustomValidationCallback is configured to accept all certificates when the flag is true

### AC3: Self-signed certificate acceptance can be disabled
- **Given** the appsettings.json contains "Server.AcceptSelfSignedCerts" set to false
- **When** the "SmartLogApi" HttpClient makes an HTTPS request to a server with a self-signed certificate
- **Then** the TLS handshake fails with an HttpRequestException containing an AuthenticationException, because the default certificate validation is used

### AC4: Warning logged when self-signed cert acceptance is active
- **Given** AcceptSelfSignedCerts is true
- **When** the application starts and configures the HttpClient
- **Then** Serilog logs a warning-level message: "Self-signed TLS certificate acceptance is enabled. This reduces TLS security and should only be used on trusted LANs."

### AC5: Polly retry policy with exponential backoff
- **Given** the "SmartLogApi" HttpClient is configured with Polly policies
- **When** an HTTP request fails with a transient error (5xx response, HttpRequestException, or TimeoutRejectedException)
- **Then** the request is retried up to 3 times with exponential backoff delays of 1 second, 2 seconds, and 4 seconds respectively, and each retry is logged via Serilog at warning level with the retry attempt number and delay

### AC6: Polly circuit breaker policy
- **Given** the "SmartLogApi" HttpClient is configured with Polly policies
- **When** 5 consecutive requests fail with transient errors
- **Then** the circuit breaker opens for 30 seconds, during which all requests immediately fail with a BrokenCircuitException without making an actual HTTP call, and the circuit breaker state change is logged via Serilog at error level

### AC7: Configurable HTTP timeout
- **Given** the appsettings.json contains "Server.TimeoutSeconds" set to 10
- **When** the "SmartLogApi" HttpClient is created
- **Then** HttpClient.Timeout is set to 10 seconds, and requests that exceed this timeout throw a TaskCanceledException

### AC8: Cross-platform compatibility
- **Given** the HTTP client infrastructure is configured in MauiProgram.cs
- **When** the application runs on macOS (MacCatalyst) or Windows (WinUI 3)
- **Then** the TLS callback, Polly policies, and timeout configuration all function correctly on both platforms without platform-specific code paths

---

## Scope

### In Scope
- Named HttpClient "SmartLogApi" registration in MauiProgram.cs via IHttpClientFactory
- HttpClientHandler with ServerCertificateCustomValidationCallback for self-signed cert acceptance
- AcceptSelfSignedCerts flag read from appsettings.json (default: true)
- Serilog warning log on startup when self-signed certs are accepted
- Polly retry policy: 3 retries with exponential backoff (1s, 2s, 4s)
- Polly circuit breaker: 5 failures threshold, 30-second break duration
- Configurable HTTP timeout from appsettings.json (default: 10 seconds)
- appsettings.json loaded as MauiAsset with IConfiguration binding
- Unit tests for policy behavior using xUnit + Moq + mock HttpMessageHandler

### Out of Scope
- Certificate pinning or custom CA bundle management
- Mutual TLS (client certificates)
- HTTP/2 or HTTP/3 configuration
- Proxy server configuration
- Custom DNS resolution
- The actual API service classes that use the HttpClient (covered by US0005 and later epics)
- Loading appsettings.json from external locations (bundled MauiAsset only)

---

## Technical Notes

### Implementation Details

**MauiProgram.cs HttpClient registration pattern:**
```csharp
// Load configuration
var config = new ConfigurationBuilder()
    .AddJsonStream(await FileSystem.OpenAppPackageFileAsync("appsettings.json"))
    .Build();
builder.Configuration.AddConfiguration(config);

// Register named HttpClient with TLS and Polly
var acceptSelfSigned = config.GetValue<bool>("Server:AcceptSelfSignedCerts", true);
var timeoutSeconds = config.GetValue<int>("Server:TimeoutSeconds", 10);

if (acceptSelfSigned)
{
    Log.Warning("Self-signed TLS certificate acceptance is enabled. " +
        "This reduces TLS security and should only be used on trusted LANs.");
}

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
    })
    .AddTransientHttpErrorPolicy(policy =>
        policy.WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1))))
    .AddTransientHttpErrorPolicy(policy =>
        policy.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));
```

**appsettings.json structure:**
```json
{
  "Server": {
    "TimeoutSeconds": 10,
    "AcceptSelfSignedCerts": true
  }
}
```

**Polly policies must be chained correctly:** retry wraps the circuit breaker, so retries happen before the circuit breaker trips. After 5 failures (including retries), the circuit opens.

### API Contracts
Not applicable (this story configures the HTTP client; it does not make API calls itself).

### Data Requirements

**appsettings.json keys consumed:**

| Key Path | Type | Default | Description |
|----------|------|---------|-------------|
| Server:TimeoutSeconds | int | 10 | HTTP request timeout in seconds |
| Server:AcceptSelfSignedCerts | bool | true | Accept self-signed TLS certificates |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Invalid certificate (not self-signed, but malformed/corrupted) | When AcceptSelfSignedCerts=true, request succeeds (callback accepts all certs). When false, request fails with HttpRequestException wrapping AuthenticationException |
| Expired certificate on server | When AcceptSelfSignedCerts=true, request succeeds. When false, request fails with certificate validation error |
| TLS handshake failure (server drops connection during handshake) | HttpRequestException thrown; Polly retry policy retries up to 3 times; if all retries fail, exception propagates to caller |
| Timeout during TLS handshake (server unresponsive on TLS port) | After 10 seconds, TaskCanceledException thrown; Polly retry policy retries with backoff; total max wait ~17 seconds (10+1+10+2+10+4) before failure |
| Circuit breaker in open state | BrokenCircuitException thrown immediately without making HTTP call; callers must handle this and present appropriate "server unavailable" messaging |
| Polly retry exhaustion (all 3 retries fail) | Original exception (HttpRequestException or TimeoutException) propagates to caller after final retry; total elapsed time depends on failure mode |
| appsettings.json missing or unreadable | Default values used: AcceptSelfSignedCerts=true, TimeoutSeconds=10; warning logged that defaults are being used |
| AcceptSelfSignedCerts key missing from appsettings.json | Defaults to true; self-signed certs accepted; warning logged |
| Server responds with HTTP 503 (Service Unavailable) | Classified as transient error by Polly; retry policy activates with exponential backoff |
| Server responds with HTTP 200 after 2 failed attempts | Third attempt succeeds; total delay is 1s + 2s = 3s of retry wait; successful response returned to caller |
| Concurrent requests while circuit breaker is half-open | First request through tests the circuit; if it succeeds, circuit closes; if it fails, circuit reopens for another 30 seconds |
| HttpClient disposed and recreated by IHttpClientFactory | IHttpClientFactory manages handler lifetime (default 2 minutes); policies and TLS config survive handler recycling |

---

## Test Scenarios

- [ ] Named HttpClient "SmartLogApi" is resolvable from IHttpClientFactory via DI
- [ ] Self-signed cert acceptance: HTTPS request to self-signed server succeeds when AcceptSelfSignedCerts=true
- [ ] Self-signed cert rejection: HTTPS request to self-signed server fails when AcceptSelfSignedCerts=false
- [ ] Startup warning log emitted when AcceptSelfSignedCerts=true
- [ ] No warning log emitted when AcceptSelfSignedCerts=false
- [ ] Retry policy: request retried up to 3 times on transient failure (mock handler returns 500, 500, 500, then verify 4 total calls)
- [ ] Retry policy: successful retry on second attempt (mock handler returns 500, then 200; verify 2 total calls)
- [ ] Retry backoff timing: delays are approximately 1s, 2s, 4s between retries
- [ ] Circuit breaker opens after 5 consecutive failures (mock handler returns 500 five times; sixth call throws BrokenCircuitException)
- [ ] Circuit breaker closes after 30-second break period (mock handler returns 500 five times, wait 30s, next call goes through)
- [ ] HTTP timeout: request exceeding 10 seconds throws TaskCanceledException (mock handler with artificial delay)
- [ ] Default timeout of 10 seconds applied when TimeoutSeconds not in appsettings.json
- [ ] Custom timeout applied when TimeoutSeconds is set (e.g., 5 seconds)
- [ ] Default AcceptSelfSignedCerts=true when key not present in appsettings.json
- [ ] appsettings.json missing entirely: defaults applied, no crash
- [ ] HttpClient includes no default Authorization headers (API key added per-request by callers)
- [ ] Polly retry logs each retry attempt at warning level via Serilog
- [ ] Circuit breaker state change logged at error level via Serilog

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| None | — | This is a foundational story with no story dependencies | — |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Microsoft.Extensions.Http (IHttpClientFactory) | NuGet Package | Available in .NET 8.0 |
| Microsoft.Extensions.Http.Polly | NuGet Package | Available (Polly 8.x integration) |
| Microsoft.Extensions.Configuration.Json | NuGet Package | Available in .NET 8.0 |
| Serilog + Serilog.Extensions.Logging | NuGet Package | Available; full logging configured in US0003, basic Serilog usable independently |
| appsettings.json bundled as MauiAsset | Build Config | Must be added to .csproj as MauiAsset |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

---

## Open Questions

- [ ] Should the Polly retry policy also handle TaskCanceledException (timeout) as a transient error for retry, or only HttpRequestException and 5xx responses? - Owner: Architect
- [ ] Should the circuit breaker count retried requests individually (i.e., 3 retries on 2 original requests = 6 failures toward the 5-failure threshold) or count original request attempts only? - Owner: Architect
- [ ] Is HTTP/2 support needed for the SmartLog server, or is HTTP/1.1 sufficient? - Owner: Product

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
