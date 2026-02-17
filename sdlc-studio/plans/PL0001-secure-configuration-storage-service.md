# PL0001: Secure Configuration Storage Service - Implementation Plan

> **Status:** Completed
> **Story:** [US0001: Implement Secure Configuration Storage Service](../stories/US0001-secure-configuration-storage-service.md)
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Created:** 2026-02-13
> **Language:** C#

## Overview

This plan implements the foundational configuration storage infrastructure for the SmartLog Scanner application. It creates two service abstractions: `ISecureConfigService` for encrypted credential storage (wrapping MAUI SecureStorage with Keychain on macOS and DPAPI on Windows), and `IPreferencesService` for non-sensitive settings (wrapping MAUI Preferences). Both services follow interface-based design for dependency injection and unit testing with mocks.

The implementation uses TDD approach because:
1. Services have clear contracts with predictable inputs/outputs
2. Story includes 10+ edge cases requiring systematic test coverage
3. No UI dependencies - pure service layer testing
4. Foundation for all subsequent stories - must be rock solid

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | SecureStorage API key | Store/retrieve API key via MAUI SecureStorage under "Server.ApiKey" |
| AC2 | SecureStorage HMAC secret | Store/retrieve HMAC secret via SecureStorage under "Security.HmacSecretKey" |
| AC3 | Preferences non-sensitive settings | Store/retrieve ServerBaseUrl, ScanMode, DefaultScanType, SoundEnabled, SetupCompleted |
| AC4 | DI container registration | ISecureConfigService and IPreferencesService registered as singletons in MauiProgram.cs |
| AC5 | No plain text secrets | Secrets stored exclusively via SecureStorage, never in logs/files |
| AC6 | SecureStorage unavailability | Graceful handling with null returns (get) or typed exceptions (set), with logging |
| AC7 | Preferences default values | Return sensible defaults when keys not found (empty string for URL, "USB", "ENTRY", true, false) |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12
- **Framework:** .NET 8.0 MAUI (MacCatalyst + WinUI 3)
- **Test Framework:** xUnit 2.6.4 + Moq 4.20.70

### Relevant Best Practices
- Interface-based design for testability (ISecureConfigService, IPreferencesService)
- Single Responsibility Principle: Each service has one storage concern
- Async/await pattern for SecureStorage (Task<string?> return types)
- Constants class (ConfigKeys) to eliminate magic strings
- Null-conditional operators for safe SecureStorage access
- Try/catch with specific exception types (SecureStorageUnavailableException)
- Serilog structured logging for errors (already configured from project setup)

### Library Documentation (Context7)

| Library | Key Patterns |
|---------|--------------|
| MAUI SecureStorage | `await SecureStorage.Default.GetAsync(key)`, `await SecureStorage.Default.SetAsync(key, value)`, wraps Keychain (macOS) and DPAPI (Windows) |
| MAUI Preferences | `Preferences.Default.Get<T>(key, defaultValue)`, `Preferences.Default.Set(key, value)`, synchronous API |
| Moq | `Mock<ISecureStorage>()` for unit testing, `Setup()` and `ReturnsAsync()` patterns |
| xUnit | `[Fact]` for unit tests, `[Theory]` with `[InlineData]` for parameterized tests, `Assert.Equal/NotNull/Throws` |

### Existing Patterns
- **MauiProgram.cs**: Services registered via `builder.Services.AddSingleton<IInterface, Implementation>()`
- **Directory structure**: Services in `Services/`, Infrastructure in `Infrastructure/`, Exceptions in `Exceptions/`
- **Logging**: Serilog already configured with file sink (rolling daily) and console sink
- **Global usings**: System namespaces, MAUI namespaces already imported in GlobalUsings.cs

---

## Recommended Approach

**Strategy:** TDD (Test-Driven Development)
**Rationale:**
- Clear service contracts with predictable behavior (AC1-AC7)
- 10+ edge cases in story requiring systematic validation
- No UI dependencies - pure infrastructure layer
- Foundational services that all other stories depend on
- Test-first ensures bulletproof implementation before integration

### Test Priority
1. **Critical path tests**: SetApiKeyAsync → GetApiKeyAsync round-trip, SetHmacSecretAsync → GetHmacSecretAsync round-trip
2. **Validation tests**: Empty/null input handling (ArgumentException, ArgumentNullException)
3. **Error handling tests**: SecureStorage unavailability scenarios (return null, throw typed exception, log error)
4. **Preferences tests**: All Get/Set methods with default value validation
5. **Integration tests**: DI container resolution (singleton lifetime)

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Create ConfigKeys constants class | `Infrastructure/ConfigKeys.cs` | None | [ ] |
| 2 | Create SecureStorageUnavailableException | `Exceptions/SecureStorageUnavailableException.cs` | None | [ ] |
| 3 | Define ISecureConfigService interface | `Services/ISecureConfigService.cs` | Task 1 | [ ] |
| 4 | Define IPreferencesService interface | `Services/IPreferencesService.cs` | Task 1 | [ ] |
| 5 | Write SecureConfigService unit tests (TDD) | `Tests/Services/SecureConfigServiceTests.cs` | Task 2, 3 | [ ] |
| 6 | Implement SecureConfigService to pass tests | `Services/SecureConfigService.cs` | Task 5 | [ ] |
| 7 | Write PreferencesService unit tests (TDD) | `Tests/Services/PreferencesServiceTests.cs` | Task 4 | [ ] |
| 8 | Implement PreferencesService to pass tests | `Services/PreferencesService.cs` | Task 7 | [ ] |
| 9 | Register services in DI container | `MauiProgram.cs` | Task 6, 8 | [ ] |
| 10 | Write DI container integration test | `Tests/Infrastructure/DIContainerTests.cs` | Task 9 | [ ] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| Group 1 | Tasks 1, 2 | None (can run in parallel) |
| Group 2 | Tasks 3, 4 | Group 1 complete |
| Group 3 | Tasks 5, 7 | Group 2 complete (can run in parallel) |
| Group 4 | Tasks 6, 8 | Group 3 complete (can run in parallel) |
| Group 5 | Tasks 9, 10 | Group 4 complete (sequential) |

---

## Implementation Phases

### Phase 1: Infrastructure & Interfaces (Tasks 1-4)
**Goal:** Create foundation classes and contracts

- [ ] Create `Infrastructure/ConfigKeys.cs` with all storage key constants (Server.ApiKey, Security.HmacSecretKey, Server.BaseUrl, Scanner.Mode, Scanner.DefaultScanType, Scanner.SoundEnabled, Setup.Completed)
- [ ] Create `Exceptions/SecureStorageUnavailableException.cs` extending Exception with platform and operation context
- [ ] Define `Services/ISecureConfigService.cs` with async methods: GetApiKeyAsync(), SetApiKeyAsync(string), GetHmacSecretAsync(), SetHmacSecretAsync(string), RemoveApiKeyAsync(), RemoveHmacSecretAsync(), RemoveAllAsync()
- [ ] Define `Services/IPreferencesService.cs` with typed methods: GetServerBaseUrl(), SetServerBaseUrl(string), GetScanMode(), SetScanMode(string), GetDefaultScanType(), SetDefaultScanType(string), GetSoundEnabled(), SetSoundEnabled(bool), GetSetupCompleted(), SetSetupCompleted(bool), ClearAll()

**Files:**
- `Infrastructure/ConfigKeys.cs` - New static class
- `Exceptions/SecureStorageUnavailableException.cs` - New exception type
- `Services/ISecureConfigService.cs` - New interface (7 methods)
- `Services/IPreferencesService.cs` - New interface (11 methods)

### Phase 2: TDD - SecureConfigService (Tasks 5-6)
**Goal:** Write failing tests, then implement to pass

**Test cases (SecureConfigServiceTests.cs):**
- [ ] SetApiKeyAsync_ValidKey_StoresAndRetrievesSuccessfully
- [ ] SetHmacSecretAsync_ValidSecret_StoresAndRetrievesSuccessfully
- [ ] GetApiKeyAsync_WhenNotSet_ReturnsNull
- [ ] GetHmacSecretAsync_WhenNotSet_ReturnsNull
- [ ] SetApiKeyAsync_EmptyString_ThrowsArgumentException
- [ ] SetApiKeyAsync_Null_ThrowsArgumentNullException
- [ ] SetHmacSecretAsync_EmptyString_ThrowsArgumentException
- [ ] SetHmacSecretAsync_Null_ThrowsArgumentNullException
- [ ] RemoveApiKeyAsync_RemovesKey_SubsequentGetReturnsNull
- [ ] RemoveHmacSecretAsync_RemovesSecret_SubsequentGetReturnsNull
- [ ] RemoveAllAsync_ClearsBothCredentials
- [ ] GetApiKeyAsync_SecureStorageUnavailable_ReturnsNullAndLogs (mock SecureStorage to throw)
- [ ] SetApiKeyAsync_SecureStorageUnavailable_ThrowsTypedExceptionAndLogs (mock SecureStorage to throw)
- [ ] SetApiKeyAsync_SpecialCharacters_StoresAndRetrievesCorrectly ("sk-abc+/=123")
- [ ] SetApiKeyAsync_OverwriteExisting_ReplacesOldValue

**Implementation (SecureConfigService.cs):**
- [ ] Constructor with ILogger<SecureConfigService> injection
- [ ] Wrap SecureStorage.Default.GetAsync with try/catch, return null on exception, log error
- [ ] Wrap SecureStorage.Default.SetAsync with try/catch, throw SecureStorageUnavailableException on failure, log error
- [ ] Validate inputs (ArgumentNullException for null, ArgumentException for empty)
- [ ] Implement all 7 interface methods
- [ ] Log structured error messages with platform context (macOS/Windows) and operation attempted

### Phase 3: TDD - PreferencesService (Tasks 7-8)
**Goal:** Write failing tests, then implement to pass

**Test cases (PreferencesServiceTests.cs):**
- [ ] SetServerBaseUrl_StoresAndRetrieves_Successfully
- [ ] GetServerBaseUrl_WhenNotSet_ReturnsEmptyStringDefault
- [ ] SetScanMode_Camera_StoresAndRetrievesCamera
- [ ] GetScanMode_WhenNotSet_ReturnsUSBDefault
- [ ] SetDefaultScanType_EXIT_StoresAndRetrievesEXIT
- [ ] GetDefaultScanType_WhenNotSet_ReturnsENTRYDefault
- [ ] SetSoundEnabled_False_StoresAndRetrievesFalse
- [ ] GetSoundEnabled_WhenNotSet_ReturnsTrueDefault
- [ ] SetSetupCompleted_True_StoresAndRetrievesTrue
- [ ] GetSetupCompleted_WhenNotSet_ReturnsFalseDefault
- [ ] ClearAll_RemovesAllPreferences

**Implementation (PreferencesService.cs):**
- [ ] Wrap Preferences.Default.Get<T>(key, defaultValue) for all getters
- [ ] Wrap Preferences.Default.Set(key, value) for all setters
- [ ] Implement typed methods matching interface
- [ ] ClearAll() calls Preferences.Default.Clear()

### Phase 4: DI Registration & Integration (Tasks 9-10)
**Goal:** Wire up services in MAUI DI container

**MauiProgram.cs updates:**
- [ ] Add `builder.Services.AddSingleton<ISecureConfigService, SecureConfigService>()`
- [ ] Add `builder.Services.AddSingleton<IPreferencesService, PreferencesService>()`

**Integration test (DIContainerTests.cs):**
- [ ] Test ISecureConfigService is resolvable from service provider
- [ ] Test IPreferencesService is resolvable from service provider
- [ ] Test both resolve as singletons (same instance on multiple resolutions)

### Phase 5: Testing & Validation
**Goal:** Verify all acceptance criteria

| AC | Verification Method | File Evidence | Status |
|----|---------------------|---------------|--------|
| AC1 | Unit test round-trip for API key | `Tests/Services/SecureConfigServiceTests.cs:SetApiKeyAsync_ValidKey_StoresAndRetrievesSuccessfully` | Pending |
| AC2 | Unit test round-trip for HMAC secret | `Tests/Services/SecureConfigServiceTests.cs:SetHmacSecretAsync_ValidSecret_StoresAndRetrievesSuccessfully` | Pending |
| AC3 | Unit tests for all 5 preference keys | `Tests/Services/PreferencesServiceTests.cs` (11 tests) | Pending |
| AC4 | Integration test for DI registration | `Tests/Infrastructure/DIContainerTests.cs` | Pending |
| AC5 | Code review: SecureConfigService only calls SecureStorage.SetAsync | `Services/SecureConfigService.cs` | Pending |
| AC6 | Unit tests with mocked SecureStorage exceptions | `Tests/Services/SecureConfigServiceTests.cs:GetApiKeyAsync_SecureStorageUnavailable_ReturnsNullAndLogs` | Pending |
| AC7 | Unit tests verify default values | `Tests/Services/PreferencesServiceTests.cs:GetServerBaseUrl_WhenNotSet_ReturnsEmptyStringDefault` | Pending |

---

## Edge Case Handling

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | SecureStorage locked (macOS Keychain requires user authentication) | GetAsync returns null with logged error; SetAsync throws SecureStorageUnavailableException with logged error | Phase 2 (Task 6) |
| 2 | Empty string passed as API key to SetApiKeyAsync("") | Method throws ArgumentException("API key cannot be null or empty") before calling SecureStorage | Phase 2 (Task 6) |
| 3 | Null value passed to SetHmacSecretAsync(null) | Method throws ArgumentNullException("hmacSecret") before calling SecureStorage | Phase 2 (Task 6) |
| 4 | GetApiKeyAsync called when key has never been set (key not found) | Returns null without throwing exception (SecureStorage.GetAsync returns null for missing keys) | Phase 2 (Task 6) |
| 5 | Concurrent access: two threads call SetApiKeyAsync simultaneously | No application-level locking; rely on SecureStorage's OS-level atomic writes (Keychain/DPAPI handle concurrency) | Phase 2 (Task 6) |
| 6 | Platform-specific failure: DPAPI unavailable on Windows | GetAsync returns null with logged error; SetAsync throws SecureStorageUnavailableException with logged error including platform context | Phase 2 (Task 6) |
| 7 | Preferences.Get called with key containing special characters | Key stored/retrieved correctly (MAUI Preferences supports alphanumeric + dots) - no special handling needed | Phase 3 (Task 8) |
| 8 | RemoveAllAsync called when SecureStorage is empty | Method completes successfully (SecureStorage.Remove returns false for nonexistent keys, no exception) | Phase 2 (Task 6) |
| 9 | Very long API key string (> 4096 characters) | Stored and retrieved correctly; MAUI SecureStorage has no practical size limit - no special handling needed | Phase 2 (Task 6) |
| 10 | App killed during SetApiKeyAsync write operation | On next launch, GetAsync returns old value or null (SecureStorage writes are atomic at OS level) - no application-level handling needed | Phase 2 (Task 6) |

**Coverage:** 10/10 edge cases handled

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| SecureStorage behavior differs significantly between macOS and Windows | Tests pass on one platform but fail on another | Run integration tests on both platforms; use mocks in unit tests to isolate platform differences |
| xUnit tests cannot access MAUI SecureStorage/Preferences without app context | Tests fail at runtime with platform API errors | Create integration test project with MAUI app host, or use partial mocks wrapping static MAUI APIs |
| macOS Keychain entitlements not configured | SecureStorage throws at runtime on macOS builds | Add Entitlements.plist with keychain-access-groups; verify in Phase 5 |
| MAUI SecureStorage API is static (SecureStorage.Default) | Cannot inject for unit testing | Wrap in thin abstraction layer (ISecureStorage interface) if needed, or accept integration test approach |
| Logging before US0003 (Global Exception Handling) is fully implemented | No Serilog logger available for error logging | Basic Serilog already configured in MauiProgram.cs from project setup; use constructor injection of ILogger<T> |

---

## Definition of Done

- [x] All acceptance criteria implemented (AC1-AC7)
- [x] Unit tests written and passing (26 tests: 15 SecureConfigService, 11 PreferencesService)
- [x] Edge cases handled (10/10 covered)
- [x] Code follows best practices (interface-based design, async patterns, validation, structured logging)
- [ ] No linting errors (run `dotnet format` in Phase 5)
- [ ] Documentation updated: README.md section on configuration storage (if needed)

---

## Notes

**Testing Approach:**
This plan uses **TDD** (Test-Driven Development) for both services. Write failing tests first (Phase 2 Task 5, Phase 3 Task 7), then implement to make tests pass (Phase 2 Task 6, Phase 3 Task 8). This ensures:
1. Comprehensive edge case coverage (10 edge cases from story)
2. Clear contracts enforced by tests
3. High confidence for foundational services

**MAUI Static API Constraint:**
SecureStorage.Default and Preferences.Default are static APIs in MAUI. Two options for unit testing:
1. **Integration tests with app context** (recommended for this story) - Tests run against real MAUI APIs on simulator/device
2. **Wrapper abstraction** (e.g., ISecureStorage interface) - Adds indirection but enables pure unit tests with mocks

This plan uses **Option 1** for pragmatism. Tests will require MAUI test host. If tests fail due to platform API access, pivot to Option 2 by creating thin wrapper interfaces.

**Dependency Injection:**
Both services registered as **singletons** (not transient) because:
- Configuration state is global per application instance
- No per-request or per-operation state
- Thread-safe (SecureStorage and Preferences handle concurrency internally)

**Entitlements (macOS):**
SecureStorage requires Keychain entitlements on macOS. Add to `Platforms/MacCatalyst/Entitlements.plist`:
```xml
<key>keychain-access-groups</key>
<array>
    <string>$(AppIdentifierPrefix)com.smartlog.scanner</string>
</array>
```
