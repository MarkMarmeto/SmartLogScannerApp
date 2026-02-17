# TS0001: Secure Configuration Storage Service

> **Status:** Draft
> **Story:** [US0001: Implement Secure Configuration Storage Service](../stories/US0001-secure-configuration-storage-service.md)
> **Plan:** [PL0001: Secure Configuration Storage Service](../plans/PL0001-secure-configuration-storage-service.md)
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Created:** 2026-02-13
> **Last Updated:** 2026-02-13

## Overview

This test specification covers unit testing for the foundational configuration storage services: `SecureConfigService` (encrypted credential storage via MAUI SecureStorage) and `PreferencesService` (non-sensitive settings via MAUI Preferences). Tests validate all 7 acceptance criteria, 10 edge cases from the story, and ensure proper error handling, validation, and DI container integration. Total: 26 test cases (15 SecureConfigService + 11 PreferencesService).

## Scope

### Stories Covered

| Story | Title | Priority |
|-------|-------|----------|
| [US0001](../stories/US0001-secure-configuration-storage-service.md) | Implement Secure Configuration Storage Service | High |

### AC Coverage Matrix

| Story | AC | Description | Test Cases | Status |
|-------|-----|-------------|------------|--------|
| US0001 | AC1 | SecureStorage stores/retrieves API key | TC001, TC003, TC014, TC015 | Pending |
| US0001 | AC2 | SecureStorage stores/retrieves HMAC secret | TC002, TC004, TC007, TC008 | Pending |
| US0001 | AC3 | Preferences stores/retrieves non-sensitive settings | TC016-TC026 | Pending |
| US0001 | AC4 | DI container registration | (Integration test outside this spec) | Pending |
| US0001 | AC5 | No plain text secrets | (Code review verification) | Pending |
| US0001 | AC6 | SecureStorage unavailability handling | TC012, TC013 | Pending |
| US0001 | AC7 | Preferences default values | TC017, TC019, TC021, TC023, TC025 | Pending |

**Coverage:** 7/7 ACs covered

### Test Types Required

| Type | Required | Rationale |
|------|----------|-----------|
| Unit | ✅ Yes | All test cases are unit tests using xUnit + Moq; services are pure logic with no external dependencies beyond MAUI storage APIs |
| Integration | ✅ Yes (minimal) | One integration test for DI container resolution (not in this spec; covered separately in DIContainerTests.cs) |
| E2E | ❌ No | Configuration services are infrastructure layer; E2E not applicable |

---

## Environment

| Requirement | Details |
|-------------|---------|
| Prerequisites | .NET 8.0 SDK, xUnit 2.6.4, Moq 4.20.70, MAUI test host (for SecureStorage/Preferences access) |
| External Services | None (mocked MAUI SecureStorage and Preferences APIs) |
| Test Data | API keys: "sk-test-abc123def456", "sk-abc+/=123"; HMAC secrets: "K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols="; URLs: "https://192.168.1.100:8443" |

---

## Test Cases

### TC001: SetApiKeyAsync stores valid API key and GetApiKeyAsync retrieves it

**Type:** Unit | **Priority:** Critical | **Story:** US0001 (AC1)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService instance with mocked SecureStorage | Service is initialized |
| When | SetApiKeyAsync("sk-test-abc123def456") is called | Method completes without exception |
| Then | GetApiKeyAsync() is called | Returns "sk-test-abc123def456" |

**Assertions:**
- [ ] SetApiKeyAsync does not throw exception
- [ ] GetApiKeyAsync returns exact value passed to SetApiKeyAsync
- [ ] SecureStorage.SetAsync was called once with key="Server.ApiKey" and value="sk-test-abc123def456"
- [ ] SecureStorage.GetAsync was called once with key="Server.ApiKey"

---

### TC002: SetHmacSecretAsync stores valid HMAC secret and GetHmacSecretAsync retrieves it

**Type:** Unit | **Priority:** Critical | **Story:** US0001 (AC2)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService instance with mocked SecureStorage | Service is initialized |
| When | SetHmacSecretAsync("K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=") is called | Method completes without exception |
| Then | GetHmacSecretAsync() is called | Returns "K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=" |

**Assertions:**
- [ ] SetHmacSecretAsync does not throw exception
- [ ] GetHmacSecretAsync returns exact value passed to SetHmacSecretAsync
- [ ] SecureStorage.SetAsync was called once with key="Security.HmacSecretKey"
- [ ] SecureStorage.GetAsync was called once with key="Security.HmacSecretKey"

---

### TC003: GetApiKeyAsync returns null when API key has never been set

**Type:** Unit | **Priority:** High | **Story:** US0001 (AC1, Edge Case 4)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService instance with mocked SecureStorage returning null for "Server.ApiKey" | Service is initialized |
| When | GetApiKeyAsync() is called without prior SetApiKeyAsync | Method completes without exception |
| Then | Method returns null | Returns null |

**Assertions:**
- [ ] GetApiKeyAsync returns null (not empty string, not exception)
- [ ] SecureStorage.GetAsync was called once with key="Server.ApiKey"

---

### TC004: GetHmacSecretAsync returns null when HMAC secret has never been set

**Type:** Unit | **Priority:** High | **Story:** US0001 (AC2, Edge Case 4)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService instance with mocked SecureStorage returning null for "Security.HmacSecretKey" | Service is initialized |
| When | GetHmacSecretAsync() is called without prior SetHmacSecretAsync | Method completes without exception |
| Then | Method returns null | Returns null |

**Assertions:**
- [ ] GetHmacSecretAsync returns null
- [ ] SecureStorage.GetAsync was called once with key="Security.HmacSecretKey"

---

### TC005: SetApiKeyAsync with empty string throws ArgumentException

**Type:** Unit | **Priority:** High | **Story:** US0001 (Edge Case 2)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService instance | Service is initialized |
| When | SetApiKeyAsync("") is called | Method throws ArgumentException |
| Then | Exception message contains "API key cannot be null or empty" | Exception is thrown with specific message |

**Assertions:**
- [ ] ArgumentException is thrown
- [ ] Exception message contains "API key cannot be null or empty"
- [ ] SecureStorage.SetAsync was NOT called (validation happens before storage)

---

### TC006: SetApiKeyAsync with null throws ArgumentNullException

**Type:** Unit | **Priority:** High | **Story:** US0001 (Edge Case 3)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService instance | Service is initialized |
| When | SetApiKeyAsync(null) is called | Method throws ArgumentNullException |
| Then | Exception parameter name is "apiKey" | Exception is thrown with specific parameter |

**Assertions:**
- [ ] ArgumentNullException is thrown
- [ ] Exception ParamName equals "apiKey"
- [ ] SecureStorage.SetAsync was NOT called

---

### TC007: SetHmacSecretAsync with empty string throws ArgumentException

**Type:** Unit | **Priority:** High | **Story:** US0001 (Edge Case 2)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService instance | Service is initialized |
| When | SetHmacSecretAsync("") is called | Method throws ArgumentException |
| Then | Exception message contains "HMAC secret cannot be null or empty" | Exception is thrown with specific message |

**Assertions:**
- [ ] ArgumentException is thrown
- [ ] Exception message contains "HMAC secret cannot be null or empty"
- [ ] SecureStorage.SetAsync was NOT called

---

### TC008: SetHmacSecretAsync with null throws ArgumentNullException

**Type:** Unit | **Priority:** High | **Story:** US0001 (Edge Case 3)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService instance | Service is initialized |
| When | SetHmacSecretAsync(null) is called | Method throws ArgumentNullException |
| Then | Exception parameter name is "hmacSecret" | Exception is thrown with specific parameter |

**Assertions:**
- [ ] ArgumentNullException is thrown
- [ ] Exception ParamName equals "hmacSecret"
- [ ] SecureStorage.SetAsync was NOT called

---

### TC009: RemoveApiKeyAsync removes API key and subsequent GetApiKeyAsync returns null

**Type:** Unit | **Priority:** Medium | **Story:** US0001

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService with API key previously set | SetApiKeyAsync has been called |
| When | RemoveApiKeyAsync() is called | Method completes without exception |
| Then | GetApiKeyAsync() is called | Returns null |

**Assertions:**
- [ ] RemoveApiKeyAsync completes without exception
- [ ] SecureStorage.Remove was called once with key="Server.ApiKey"
- [ ] GetApiKeyAsync returns null after removal

---

### TC010: RemoveHmacSecretAsync removes HMAC secret and subsequent GetHmacSecretAsync returns null

**Type:** Unit | **Priority:** Medium | **Story:** US0001

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService with HMAC secret previously set | SetHmacSecretAsync has been called |
| When | RemoveHmacSecretAsync() is called | Method completes without exception |
| Then | GetHmacSecretAsync() is called | Returns null |

**Assertions:**
- [ ] RemoveHmacSecretAsync completes without exception
- [ ] SecureStorage.Remove was called once with key="Security.HmacSecretKey"
- [ ] GetHmacSecretAsync returns null after removal

---

### TC011: RemoveAllAsync clears both API key and HMAC secret from SecureStorage

**Type:** Unit | **Priority:** Medium | **Story:** US0001 (Edge Case 8)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService with both API key and HMAC secret set | Both SetApiKeyAsync and SetHmacSecretAsync have been called |
| When | RemoveAllAsync() is called | Method completes without exception |
| Then | GetApiKeyAsync() and GetHmacSecretAsync() both return null | Both return null |

**Assertions:**
- [ ] RemoveAllAsync completes without exception
- [ ] SecureStorage.Remove was called twice (once for "Server.ApiKey", once for "Security.HmacSecretKey")
- [ ] GetApiKeyAsync returns null after RemoveAllAsync
- [ ] GetHmacSecretAsync returns null after RemoveAllAsync

---

### TC012: GetApiKeyAsync when SecureStorage is unavailable returns null and logs error

**Type:** Unit | **Priority:** High | **Story:** US0001 (AC6, Edge Case 1, 6)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService with mocked SecureStorage that throws exception on GetAsync | SecureStorage.GetAsync throws (simulating Keychain locked on macOS or DPAPI failure on Windows) |
| When | GetApiKeyAsync() is called | Method completes without throwing |
| Then | Method returns null and error is logged | Returns null, logs error with platform context |

**Assertions:**
- [ ] GetApiKeyAsync returns null (does not throw exception)
- [ ] ILogger.LogError was called once with message containing "SecureStorage unavailable" and platform context
- [ ] Exception details are included in log message

---

### TC013: SetApiKeyAsync when SecureStorage is unavailable throws SecureStorageUnavailableException and logs error

**Type:** Unit | **Priority:** High | **Story:** US0001 (AC6, Edge Case 1, 6)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService with mocked SecureStorage that throws exception on SetAsync | SecureStorage.SetAsync throws (simulating Keychain locked on macOS or DPAPI failure on Windows) |
| When | SetApiKeyAsync("sk-test-abc123def456") is called | Method throws SecureStorageUnavailableException |
| Then | Exception is thrown and error is logged | Throws SecureStorageUnavailableException, logs error |

**Assertions:**
- [ ] SecureStorageUnavailableException is thrown
- [ ] Exception message contains platform context (macOS/Windows)
- [ ] ILogger.LogError was called once with message containing "SecureStorage unavailable" and operation attempted
- [ ] Inner exception is the original SecureStorage exception

---

### TC014: SetApiKeyAsync with special characters stores and retrieves correctly

**Type:** Unit | **Priority:** Medium | **Story:** US0001

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService instance | Service is initialized |
| When | SetApiKeyAsync("sk-abc+/=123") is called (contains +, /, =) | Method completes without exception |
| Then | GetApiKeyAsync() is called | Returns "sk-abc+/=123" exactly |

**Assertions:**
- [ ] SetApiKeyAsync does not throw exception
- [ ] GetApiKeyAsync returns exact value including special characters
- [ ] No encoding/decoding issues

---

### TC015: SetApiKeyAsync overwrites existing API key with new value

**Type:** Unit | **Priority:** Medium | **Story:** US0001

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | SecureConfigService with API key already set to "old-key" | SetApiKeyAsync("old-key") has been called |
| When | SetApiKeyAsync("new-key") is called | Method completes without exception |
| Then | GetApiKeyAsync() is called | Returns "new-key" (old value is replaced) |

**Assertions:**
- [ ] GetApiKeyAsync returns "new-key" (not "old-key")
- [ ] SecureStorage.SetAsync was called twice (once for each key)
- [ ] Second write replaces first write

---

### TC016: PreferencesService SetServerBaseUrl stores value and GetServerBaseUrl retrieves it

**Type:** Unit | **Priority:** Critical | **Story:** US0001 (AC3)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance with mocked Preferences | Service is initialized |
| When | SetServerBaseUrl("https://192.168.1.100:8443") is called | Method completes without exception |
| Then | GetServerBaseUrl() is called | Returns "https://192.168.1.100:8443" |

**Assertions:**
- [ ] SetServerBaseUrl does not throw exception
- [ ] GetServerBaseUrl returns exact value passed to SetServerBaseUrl
- [ ] Preferences.Set was called once with key="Server.BaseUrl"
- [ ] Preferences.Get was called once with key="Server.BaseUrl"

---

### TC017: PreferencesService GetServerBaseUrl returns empty string when not set

**Type:** Unit | **Priority:** High | **Story:** US0001 (AC7)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance with mocked Preferences returning default value | Preferences.Get returns "" for "Server.BaseUrl" |
| When | GetServerBaseUrl() is called without prior SetServerBaseUrl | Method completes without exception |
| Then | Method returns "" | Returns empty string (default) |

**Assertions:**
- [ ] GetServerBaseUrl returns "" (empty string default)
- [ ] Preferences.Get was called with key="Server.BaseUrl" and defaultValue=""

---

### TC018: PreferencesService SetScanMode stores "Camera" and GetScanMode retrieves it

**Type:** Unit | **Priority:** Critical | **Story:** US0001 (AC3)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance | Service is initialized |
| When | SetScanMode("Camera") is called | Method completes without exception |
| Then | GetScanMode() is called | Returns "Camera" |

**Assertions:**
- [ ] GetScanMode returns "Camera"
- [ ] Preferences.Set was called with key="Scanner.Mode" and value="Camera"
- [ ] Preferences.Get was called with key="Scanner.Mode"

---

### TC019: PreferencesService GetScanMode returns "USB" default when not set

**Type:** Unit | **Priority:** High | **Story:** US0001 (AC7)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance with mocked Preferences | Preferences.Get returns "USB" for "Scanner.Mode" |
| When | GetScanMode() is called without prior SetScanMode | Method completes without exception |
| Then | Method returns "USB" | Returns "USB" (default) |

**Assertions:**
- [ ] GetScanMode returns "USB" (default)
- [ ] Preferences.Get was called with key="Scanner.Mode" and defaultValue="USB"

---

### TC020: PreferencesService SetDefaultScanType stores "EXIT" and GetDefaultScanType retrieves it

**Type:** Unit | **Priority:** Critical | **Story:** US0001 (AC3)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance | Service is initialized |
| When | SetDefaultScanType("EXIT") is called | Method completes without exception |
| Then | GetDefaultScanType() is called | Returns "EXIT" |

**Assertions:**
- [ ] GetDefaultScanType returns "EXIT"
- [ ] Preferences.Set was called with key="Scanner.DefaultScanType" and value="EXIT"

---

### TC021: PreferencesService GetDefaultScanType returns "ENTRY" default when not set

**Type:** Unit | **Priority:** High | **Story:** US0001 (AC7)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance | Preferences.Get returns "ENTRY" for "Scanner.DefaultScanType" |
| When | GetDefaultScanType() is called without prior SetDefaultScanType | Method completes without exception |
| Then | Method returns "ENTRY" | Returns "ENTRY" (default) |

**Assertions:**
- [ ] GetDefaultScanType returns "ENTRY" (default)
- [ ] Preferences.Get was called with key="Scanner.DefaultScanType" and defaultValue="ENTRY"

---

### TC022: PreferencesService SetSoundEnabled stores false and GetSoundEnabled retrieves it

**Type:** Unit | **Priority:** Critical | **Story:** US0001 (AC3)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance | Service is initialized |
| When | SetSoundEnabled(false) is called | Method completes without exception |
| Then | GetSoundEnabled() is called | Returns false |

**Assertions:**
- [ ] GetSoundEnabled returns false
- [ ] Preferences.Set was called with key="Scanner.SoundEnabled" and value=false

---

### TC023: PreferencesService GetSoundEnabled returns true default when not set

**Type:** Unit | **Priority:** High | **Story:** US0001 (AC7)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance | Preferences.Get returns true for "Scanner.SoundEnabled" |
| When | GetSoundEnabled() is called without prior SetSoundEnabled | Method completes without exception |
| Then | Method returns true | Returns true (default) |

**Assertions:**
- [ ] GetSoundEnabled returns true (default)
- [ ] Preferences.Get was called with key="Scanner.SoundEnabled" and defaultValue=true

---

### TC024: PreferencesService SetSetupCompleted stores true and GetSetupCompleted retrieves it

**Type:** Unit | **Priority:** Critical | **Story:** US0001 (AC3)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance | Service is initialized |
| When | SetSetupCompleted(true) is called | Method completes without exception |
| Then | GetSetupCompleted() is called | Returns true |

**Assertions:**
- [ ] GetSetupCompleted returns true
- [ ] Preferences.Set was called with key="Setup.Completed" and value=true

---

### TC025: PreferencesService GetSetupCompleted returns false default when not set

**Type:** Unit | **Priority:** High | **Story:** US0001 (AC7)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService instance | Preferences.Get returns false for "Setup.Completed" |
| When | GetSetupCompleted() is called without prior SetSetupCompleted | Method completes without exception |
| Then | Method returns false | Returns false (default) |

**Assertions:**
- [ ] GetSetupCompleted returns false (default)
- [ ] Preferences.Get was called with key="Setup.Completed" and defaultValue=false

---

### TC026: PreferencesService ClearAll removes all stored preferences

**Type:** Unit | **Priority:** Medium | **Story:** US0001 (AC3)

| Step | Action | Expected Result |
|------|--------|-----------------|
| Given | PreferencesService with multiple preferences set | SetServerBaseUrl, SetScanMode, SetDefaultScanType, SetSoundEnabled, SetSetupCompleted have all been called |
| When | ClearAll() is called | Method completes without exception |
| Then | All Get methods return default values | All preferences are cleared |

**Assertions:**
- [ ] ClearAll completes without exception
- [ ] Preferences.Clear was called once
- [ ] GetServerBaseUrl returns "" (default)
- [ ] GetScanMode returns "USB" (default)
- [ ] GetDefaultScanType returns "ENTRY" (default)
- [ ] GetSoundEnabled returns true (default)
- [ ] GetSetupCompleted returns false (default)

---

## Fixtures

```yaml
# Fixture data for US0001 tests

api_keys:
  valid:
    - "sk-test-abc123def456"
    - "sk-prod-xyz789uvw012"
  with_special_chars:
    - "sk-abc+/=123"
  invalid:
    - ""      # Empty string (should throw ArgumentException)
    - null    # Null value (should throw ArgumentNullException)

hmac_secrets:
  valid:
    - "K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols="
    - "dGVzdC1obWFjLXNlY3JldC1rZXktZm9yLXRlc3Rpbmc="
  invalid:
    - ""      # Empty string (should throw ArgumentException)
    - null    # Null value (should throw ArgumentNullException)

server_urls:
  valid:
    - "https://192.168.1.100:8443"
    - "https://smartlog.school.edu"
    - "http://localhost:5000"
  default: ""

scan_modes:
  valid:
    - "Camera"
    - "USB"
  default: "USB"

scan_types:
  valid:
    - "ENTRY"
    - "EXIT"
  default: "ENTRY"

sound_enabled:
  valid:
    - true
    - false
  default: true

setup_completed:
  valid:
    - true
    - false
  default: false

config_keys:
  secure:
    api_key: "Server.ApiKey"
    hmac_secret: "Security.HmacSecretKey"
  preferences:
    server_url: "Server.BaseUrl"
    scan_mode: "Scanner.Mode"
    scan_type: "Scanner.DefaultScanType"
    sound: "Scanner.SoundEnabled"
    setup: "Setup.Completed"
```

---

## Automation Status

| TC | Title | Status | Implementation |
|----|-------|--------|----------------|
| TC001 | SetApiKeyAsync stores and retrieves valid API key | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC002 | SetHmacSecretAsync stores and retrieves valid HMAC secret | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC003 | GetApiKeyAsync returns null when not set | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC004 | GetHmacSecretAsync returns null when not set | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC005 | SetApiKeyAsync with empty string throws ArgumentException | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC006 | SetApiKeyAsync with null throws ArgumentNullException | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC007 | SetHmacSecretAsync with empty string throws ArgumentException | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC008 | SetHmacSecretAsync with null throws ArgumentNullException | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC009 | RemoveApiKeyAsync removes API key | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC010 | RemoveHmacSecretAsync removes HMAC secret | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC011 | RemoveAllAsync clears both credentials | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC012 | GetApiKeyAsync handles SecureStorage unavailability | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC013 | SetApiKeyAsync handles SecureStorage unavailability | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC014 | SetApiKeyAsync with special characters | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC015 | SetApiKeyAsync overwrites existing API key | Pending | SmartLog.Scanner.Tests/Services/SecureConfigServiceTests.cs |
| TC016 | PreferencesService SetServerBaseUrl and GetServerBaseUrl | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC017 | PreferencesService GetServerBaseUrl returns empty string default | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC018 | PreferencesService SetScanMode and GetScanMode | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC019 | PreferencesService GetScanMode returns USB default | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC020 | PreferencesService SetDefaultScanType and GetDefaultScanType | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC021 | PreferencesService GetDefaultScanType returns ENTRY default | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC022 | PreferencesService SetSoundEnabled and GetSoundEnabled | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC023 | PreferencesService GetSoundEnabled returns true default | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC024 | PreferencesService SetSetupCompleted and GetSetupCompleted | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC025 | PreferencesService GetSetupCompleted returns false default | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |
| TC026 | PreferencesService ClearAll removes all preferences | Pending | SmartLog.Scanner.Tests/Services/PreferencesServiceTests.cs |

---

## Traceability

| Artefact | Reference |
|----------|-----------|
| PRD | [sdlc-studio/prd.md](../prd.md) |
| Epic | [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md) |
| Story | [US0001: Implement Secure Configuration Storage Service](../stories/US0001-secure-configuration-storage-service.md) |
| Plan | [PL0001: Secure Configuration Storage Service](../plans/PL0001-secure-configuration-storage-service.md) |
| TSD | (TSD not yet created) |

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial spec created with 26 test cases for US0001 |
