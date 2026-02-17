# US0001: Implement Secure Configuration Storage Service

> **Status:** Done
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Completed:** 2026-02-13

## User Story

**As a** system (benefiting IT Admin Ian)
**I want** encrypted credential storage and preference management through well-defined service interfaces
**So that** API keys and HMAC secrets are never exposed in plain text, and non-sensitive settings are persisted reliably across app restarts on both macOS and Windows

## Context

### Persona Reference
**IT Admin Ian** - School IT administrator, intermediate technical proficiency, deploys and configures scanner devices across school gates. Needs confidence that credentials entered during setup are stored securely and persist reliably.
[Full persona details](../personas.md#it-admin-ian)

### Background
SmartLog Scanner must store two categories of configuration data: sensitive credentials (API key, HMAC secret) that require platform-native encryption, and non-sensitive runtime preferences (server URL, scan mode, scan type, sound toggle, setup completion flag) that need simple persistent key-value storage. This story creates the foundational storage abstractions that every other story in the application depends on. The interfaces (ISecureConfigService, IPreferencesService) enable dependency injection and unit testing with mocks, while the concrete implementations wrap MAUI SecureStorage (Keychain on macOS, DPAPI on Windows) and MAUI Preferences respectively.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Security | API key and HMAC secret must use MAUI SecureStorage (Keychain/DPAPI) | SecureConfigService must wrap SecureStorage, not file-based storage |
| PRD | Security | No secrets in plain text config files, logs, or source code | Service must never log secret values; keys stored only via SecureStorage |
| TRD | Architecture | MVVM with layered services, DI via built-in MAUI container | Must define ISecureConfigService and IPreferencesService interfaces for DI registration |
| TRD | Architecture | Interface-based design for testability | Concrete implementations must be swappable with mocks in unit tests |
| TRD | Tech Stack | .NET 8.0 MAUI targeting macOS (MacCatalyst) + Windows (WinUI 3) | SecureStorage behaviour differs per platform; must handle both |

---

## Acceptance Criteria

### AC1: SecureStorage stores and retrieves API key
- **Given** the application is running on macOS or Windows
- **When** ISecureConfigService.SetApiKeyAsync("sk-test-abc123def456") is called
- **Then** the value is persisted in MAUI SecureStorage under the key "Server.ApiKey" (Keychain on macOS, DPAPI on Windows), and a subsequent call to GetApiKeyAsync() returns "sk-test-abc123def456"

### AC2: SecureStorage stores and retrieves HMAC secret
- **Given** the application is running on macOS or Windows
- **When** ISecureConfigService.SetHmacSecretAsync("K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=") is called
- **Then** the value is persisted in MAUI SecureStorage under the key "Security.HmacSecretKey", and a subsequent call to GetHmacSecretAsync() returns "K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols="

### AC3: Preferences stores and retrieves non-sensitive settings
- **Given** the application is running on macOS or Windows
- **When** IPreferencesService is used to set the following values:
  - ServerBaseUrl = "https://192.168.1.100:8443"
  - ScanMode = "Camera"
  - DefaultScanType = "ENTRY"
  - SoundEnabled = true
  - SetupCompleted = true
- **Then** each value is persisted in MAUI Preferences under its respective key ("Server.BaseUrl", "Scanner.Mode", "Scanner.DefaultScanType", "Scanner.SoundEnabled", "Setup.Completed"), and subsequent get calls return the stored values

### AC4: Interface-based design registered in DI container
- **Given** MauiProgram.cs configures the DI container
- **When** the app starts
- **Then** ISecureConfigService is registered as a singleton mapping to SecureConfigService, and IPreferencesService is registered as a singleton mapping to PreferencesService, both resolvable via constructor injection

### AC5: Secrets are never stored in plain text
- **Given** ISecureConfigService stores an API key or HMAC secret
- **When** the storage operation completes
- **Then** the value is stored exclusively via MAUI SecureStorage (which delegates to Keychain on macOS and DPAPI on Windows) and is NOT written to appsettings.json, MAUI Preferences, log files, or any other plain text location

### AC6: SecureStorage unavailability handled gracefully
- **Given** MAUI SecureStorage is unavailable (e.g., Keychain locked on macOS, DPAPI failure on Windows)
- **When** any ISecureConfigService method is called
- **Then** the method does not throw an unhandled exception; instead it returns null (for get operations) or returns false/throws a typed SecureStorageUnavailableException (for set operations), and the error is logged via Serilog with a descriptive message including the platform and operation attempted

### AC7: Preferences returns default values when key not found
- **Given** no value has been previously stored for a given preference key
- **When** IPreferencesService.GetServerBaseUrl() is called
- **Then** it returns an empty string (the specified default), and similarly GetScanMode() returns "USB", GetDefaultScanType() returns "ENTRY", GetSoundEnabled() returns true, and GetSetupCompleted() returns false

---

## Scope

### In Scope
- ISecureConfigService interface with async methods: GetApiKeyAsync(), SetApiKeyAsync(), GetHmacSecretAsync(), SetHmacSecretAsync(), RemoveApiKeyAsync(), RemoveHmacSecretAsync(), RemoveAllAsync()
- SecureConfigService concrete implementation wrapping MAUI SecureStorage
- IPreferencesService interface with typed methods for each preference key: Get/Set for ServerBaseUrl, ScanMode, DefaultScanType, SoundEnabled, SetupCompleted, plus ClearAll()
- PreferencesService concrete implementation wrapping MAUI Preferences
- DI registration in MauiProgram.cs (singleton lifetime)
- Constants class defining all storage key strings (e.g., ConfigKeys.ApiKey = "Server.ApiKey")
- Unit tests for both services using xUnit + Moq

### Out of Scope
- Settings UI page (covered by US0004)
- Connection testing (covered by US0005)
- Migration from one storage mechanism to another
- Remote configuration management
- Encryption key rotation
- Biometric authentication for SecureStorage access

---

## Technical Notes

### Implementation Details
- **SecureConfigService** wraps `SecureStorage.Default` (MAUI static API). For testability, accept `ISecureStorage` via constructor injection if MAUI supports it; otherwise wrap the static calls and test via integration tests on each platform.
- **PreferencesService** wraps `Preferences.Default` (MAUI static API). Same testability pattern as above.
- **ConfigKeys** static class centralizes all key strings to avoid magic strings throughout the codebase:
  ```csharp
  public static class ConfigKeys
  {
      public const string ApiKey = "Server.ApiKey";
      public const string HmacSecretKey = "Security.HmacSecretKey";
      public const string ServerBaseUrl = "Server.BaseUrl";
      public const string ScanMode = "Scanner.Mode";
      public const string DefaultScanType = "Scanner.DefaultScanType";
      public const string SoundEnabled = "Scanner.SoundEnabled";
      public const string SetupCompleted = "Setup.Completed";
  }
  ```
- All SecureStorage calls are async (Task<string?>, Task). Wrap in try/catch to handle platform-specific exceptions.
- On macOS: SecureStorage uses Keychain. Entitlements must include `keychain-access-groups`.
- On Windows: SecureStorage uses DPAPI. No special manifest entries required.

### API Contracts
Not applicable (no HTTP calls in this story).

### Data Requirements

**Secure Storage Keys:**

| Key | Type | Description |
|-----|------|-------------|
| Server.ApiKey | string | Encrypted API key for X-API-Key header |
| Security.HmacSecretKey | string | Encrypted HMAC-SHA256 shared secret |

**Preference Keys:**

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| Server.BaseUrl | string | "" | SmartLog server URL |
| Scanner.Mode | string | "USB" | Scan input mode (Camera/USB) |
| Scanner.DefaultScanType | string | "ENTRY" | Default scan direction |
| Scanner.SoundEnabled | bool | true | Audio feedback toggle |
| Setup.Completed | bool | false | First-launch guard flag |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| SecureStorage locked (macOS Keychain requires user authentication) | GetApiKeyAsync/GetHmacSecretAsync return null; SetApiKeyAsync/SetHmacSecretAsync throw SecureStorageUnavailableException; error logged with message "SecureStorage unavailable on macOS: Keychain access denied" |
| Empty string passed as API key to SetApiKeyAsync("") | Method throws ArgumentException("API key cannot be null or empty"); value is not stored |
| Null value passed as HMAC secret to SetHmacSecretAsync(null) | Method throws ArgumentNullException("hmacSecret"); value is not stored |
| GetApiKeyAsync called when key has never been set (key not found) | Returns null without throwing an exception |
| Concurrent access: two threads call SetApiKeyAsync simultaneously | SecureStorage handles concurrency at the OS level; second write wins; no data corruption; operation is atomic from the caller's perspective |
| Platform-specific failure: DPAPI unavailable on Windows in certain service contexts | GetApiKeyAsync returns null; error logged with message "SecureStorage unavailable on Windows: DPAPI access failed"; application does not crash |
| Preferences.Get called with key that contains special characters | Key is stored and retrieved correctly (MAUI Preferences supports alphanumeric keys with dots) |
| RemoveAllAsync called when SecureStorage is empty | Method completes successfully without throwing; no-op for nonexistent keys |
| Very long API key string (> 4096 characters) | Stored and retrieved correctly; MAUI SecureStorage has no practical size limit for string values on either platform |
| App killed during SetApiKeyAsync write operation | On next launch, GetApiKeyAsync returns either the old value or null (never a partial/corrupted value); SecureStorage writes are atomic at the OS level |

---

## Test Scenarios

- [ ] SetApiKeyAsync stores value and GetApiKeyAsync retrieves the same value
- [ ] SetHmacSecretAsync stores value and GetHmacSecretAsync retrieves the same value
- [ ] GetApiKeyAsync returns null when no API key has been stored
- [ ] GetHmacSecretAsync returns null when no HMAC secret has been stored
- [ ] SetApiKeyAsync with empty string throws ArgumentException
- [ ] SetApiKeyAsync with null throws ArgumentNullException
- [ ] SetHmacSecretAsync with empty string throws ArgumentException
- [ ] SetHmacSecretAsync with null throws ArgumentNullException
- [ ] RemoveApiKeyAsync removes the stored API key; subsequent GetApiKeyAsync returns null
- [ ] RemoveHmacSecretAsync removes the stored HMAC secret; subsequent GetHmacSecretAsync returns null
- [ ] RemoveAllAsync clears both API key and HMAC secret from SecureStorage
- [ ] SecureStorage unavailability: GetApiKeyAsync returns null and logs error (mock SecureStorage to throw)
- [ ] SecureStorage unavailability: SetApiKeyAsync throws SecureStorageUnavailableException and logs error
- [ ] PreferencesService.SetServerBaseUrl stores value and GetServerBaseUrl retrieves it
- [ ] PreferencesService.GetServerBaseUrl returns "" when no value has been set
- [ ] PreferencesService.SetScanMode("Camera") and GetScanMode() returns "Camera"
- [ ] PreferencesService.GetScanMode returns "USB" as default when no value set
- [ ] PreferencesService.SetDefaultScanType("EXIT") and GetDefaultScanType() returns "EXIT"
- [ ] PreferencesService.GetDefaultScanType returns "ENTRY" as default when no value set
- [ ] PreferencesService.SetSoundEnabled(false) and GetSoundEnabled() returns false
- [ ] PreferencesService.GetSoundEnabled returns true as default when no value set
- [ ] PreferencesService.SetSetupCompleted(true) and GetSetupCompleted() returns true
- [ ] PreferencesService.GetSetupCompleted returns false as default when no value set
- [ ] PreferencesService.ClearAll removes all stored preferences
- [ ] ISecureConfigService is resolvable from the DI container as a singleton
- [ ] IPreferencesService is resolvable from the DI container as a singleton
- [ ] API key with special characters (e.g., "sk-abc+/=123") is stored and retrieved correctly
- [ ] Overwriting an existing API key replaces the old value

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| None | — | This is a foundational story with no story dependencies | — |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| MAUI SecureStorage API | Platform SDK | Available in .NET 8.0 MAUI |
| MAUI Preferences API | Platform SDK | Available in .NET 8.0 MAUI |
| macOS Keychain entitlements | Platform Config | Must be configured in Entitlements.plist |
| Serilog (for error logging) | NuGet Package | Available; configured in US0003 but basic logging can be added independently |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

---

## Open Questions

- [ ] Should SecureConfigService implement a fallback strategy when SecureStorage is permanently unavailable (e.g., in-memory only mode), or should it block all operations requiring credentials? - Owner: Architect
- [ ] Is there a maximum length constraint on API keys or HMAC secrets from the SmartLog Admin Web App? - Owner: Product
- [ ] Should RemoveAllAsync also clear MAUI Preferences, or only SecureStorage entries? - Owner: Architect

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
