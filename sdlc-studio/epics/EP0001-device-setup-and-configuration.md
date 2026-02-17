# EP0001: Device Setup and Configuration

> **Status:** Done
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Completed:** 2026-02-13
> **Target Release:** 1.0.0

## Summary

Enable IT administrators to install, configure, and maintain SmartLog Scanner devices on school gate machines. This epic covers the first-launch setup wizard, encrypted credential storage, self-signed TLS certificate support, and global crash recovery — the foundational infrastructure that all other epics depend on.

## Inherited Constraints

> See PRD and TRD for full constraint details. Key constraints for this epic:

| Source | Type | Constraint | Impact |
|--------|------|------------|--------|
| PRD | Security | API key and HMAC secret must use MAUI SecureStorage (Keychain/DPAPI) | F12 must abstract platform-specific secure storage |
| PRD | Security | No secrets in plain text config files, logs, or source code | Setup wizard must write to SecureStorage, not appsettings.json |
| TRD | Architecture | MVVM with layered services, DI via built-in MAUI container | ISecureConfigService interface required for testability |
| TRD | Tech Stack | .NET 8.0 MAUI targeting macOS (MacCatalyst) + Windows (WinUI 3) | TLS and SecureStorage behavior differs per platform |

---

## Business Context

### Problem Statement
Before a scanner device can process any QR codes, it must be configured with the correct server URL, API key, and HMAC secret. IT administrators need a one-time setup process that is straightforward, validates connectivity, and stores credentials securely. Without this foundation, no other feature can operate.

**PRD Reference:** [Feature Inventory](../prd.md#3-feature-inventory)

### Value Proposition
A robust setup and configuration layer means IT Admin Ian configures each gate device once and rarely revisits it. Secure credential storage prevents accidental exposure. Global exception handling and logging ensure that when issues do arise, they are diagnosable without on-site visits.

### Success Metrics

| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Setup completion rate | N/A | 100% first attempt (with valid credentials) | Test with correct server URL + API key |
| Configuration-related support calls | N/A | 0 after initial setup | IT support ticket tracking |
| Crash recovery success | N/A | 100% auto-recovery | App restarts cleanly after forced termination |

---

## Scope

### In Scope
- First-launch setup wizard (server URL, API key, HMAC secret, scan mode, default scan type)
- "Test Connection" validation via GET /api/v1/health/details
- SecureStorage abstraction (ISecureConfigService) for API key and HMAC secret
- MAUI Preferences for non-sensitive settings (server URL, scan mode, etc.)
- Self-signed TLS certificate acceptance with configurable toggle
- Named HttpClient registration via IHttpClientFactory in MauiProgram.cs
- Global exception handling (AppDomain, TaskScheduler) with Serilog logging
- Shell navigation guard (Setup.Completed → SetupPage or MainPage)
- Log files at FileSystem.AppDataDirectory/logs/

### Out of Scope
- Remote device management or reconfiguration
- Automatic device provisioning from admin panel
- Certificate pinning or custom CA bundle management
- Multi-device fleet management dashboard

### Affected Personas
- **IT Admin Ian:** Primary user — configures devices, troubleshoots connectivity, needs clear error messages during setup
- **Guard Gary:** Indirect — benefits from a device that "just works" after setup and recovers from crashes

---

## Acceptance Criteria (Epic Level)

- [ ] First launch with no configuration shows SetupPage automatically
- [ ] Setup wizard collects server URL, API key, HMAC secret, scan mode, and default scan type
- [ ] "Test Connection" validates server connectivity and API key via GET /api/v1/health/details
- [ ] On successful setup, API key and HMAC secret are stored in MAUI SecureStorage
- [ ] Non-sensitive settings stored in MAUI Preferences
- [ ] Subsequent launches navigate directly to MainPage (Setup.Completed = true)
- [ ] Self-signed TLS certificates accepted when AcceptSelfSignedCerts = true
- [ ] HttpClient registered via IHttpClientFactory with TLS configuration
- [ ] Unhandled exceptions captured by global handlers and logged via Serilog
- [ ] Log files written to FileSystem.AppDataDirectory/logs/
- [ ] App recovers gracefully from crashes — no data corruption, pending scans preserved

---

## Dependencies

### Blocked By

| Dependency | Type | Status | Owner |
|------------|------|--------|-------|
| None — foundational epic | — | — | — |

### Blocking

| Item | Type | Impact |
|------|------|--------|
| EP0002: QR Code Scanning & Validation | Epic | Needs F12 (SecureStorage) for HMAC secret retrieval |
| EP0003: Scan Processing & Feedback | Epic | Needs F12 (API key) and F13 (TLS) for server communication |
| EP0004: Offline Resilience & Sync | Epic | Needs F13 (TLS) for health check connectivity |

---

## Risks & Assumptions

### Assumptions
- MAUI SecureStorage works reliably on both macOS (Keychain) and Windows (DPAPI) for .NET 8.0
- IT administrators have valid API keys generated from the SmartLog Admin Web App before setup
- School LAN uses HTTPS (possibly self-signed) for server communication
- Serilog file sink handles concurrent writes from background threads safely

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SecureStorage unavailable (Keychain locked, DPAPI failure) | Low | High | Detect and display clear error; log for IT diagnosis |
| Self-signed TLS acceptance introduces security warnings on macOS | Medium | Low | Log warning; document expected behavior for IT |
| Setup wizard fields validated incorrectly (bad URL format) | Low | Medium | Input validation with specific error messages per field |
| Global exception handler misses platform-specific exceptions | Low | Medium | Test on both platforms; add platform-specific handlers if needed |

---

## Technical Considerations

### Architecture Impact
- Establishes the DI registration pattern in MauiProgram.cs used by all subsequent epics
- Defines the ISecureConfigService and IPreferencesService interfaces consumed throughout the app
- Sets up Shell navigation routing (SetupPage ↔ MainPage)
- Configures IHttpClientFactory with Polly policies and TLS settings used by all HTTP services
- Configures Serilog logging pipeline used by all services

### Integration Points
- GET /api/v1/health/details — authenticated health check during setup validation
- MAUI SecureStorage — platform-specific encrypted credential storage
- MAUI Preferences — cross-platform key-value settings
- Serilog — structured logging to file and console sinks

---

## Sizing

**Story Points:** 23
**Estimated Story Count:** 5

**Complexity Factors:**
- Cross-platform SecureStorage behavior differences (macOS Keychain vs Windows DPAPI)
- HttpClient + IHttpClientFactory + Polly + TLS configuration in MauiProgram.cs
- Shell navigation guard logic (conditional routing based on setup state)
- Global exception handling across AppDomain and TaskScheduler boundaries

---

## Story Breakdown

- [ ] [US0001: Implement Secure Configuration Storage Service](../stories/US0001-secure-configuration-storage-service.md)
- [ ] [US0002: Configure Self-Signed TLS and HTTP Client Infrastructure](../stories/US0002-self-signed-tls-and-http-client-infrastructure.md)
- [ ] [US0003: Implement Global Exception Handling and Logging](../stories/US0003-global-exception-handling-and-logging.md)
- [ ] [US0004: Build Device Setup Wizard Page](../stories/US0004-device-setup-wizard-page.md)
- [ ] [US0005: Implement Setup Connection Validation](../stories/US0005-setup-connection-validation.md)

---

## Test Plan

> Test spec to be generated via `/sdlc-studio test-spec --epic EP0001`

---

## Open Questions

- [ ] What happens when SecureStorage is unavailable (Keychain locked on macOS)? Fallback strategy needed. - Owner: Architect
- [ ] Should setup wizard support re-configuration after initial setup (settings page)? - Owner: Product

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial epic created from PRD features F01, F12, F13, F15 |
