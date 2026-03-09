# US0006: Implement Local HMAC-SHA256 QR Validation

> **Status:** Done
> **Epic:** [EP0002: QR Code Scanning and Validation](../epics/EP0002-qr-code-scanning-and-validation.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Completed:** 2026-02-16

## User Story

**As a** System (with Guard Gary as the primary beneficiary)
**I want** the app to locally validate QR code HMAC-SHA256 signatures before any server contact
**So that** forged or tampered QR codes are rejected instantly (< 100ms) without wasting network bandwidth, and Guard Gary gets immediate red-light feedback for invalid codes

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency, needs instant visual feedback (green = good, red = bad) with zero decision-making during scanning.
[Full persona details](../personas.md#guard-gary)

### Background
Student QR codes follow the format `SMARTLOG:{studentId}:{unixTimestamp}:{hmacBase64}` where the HMAC-SHA256 is computed over `{studentId}:{timestamp}` using a shared secret key stored in MAUI SecureStorage (provisioned during device setup via EP0001). This service is the gatekeeper: every QR payload from camera (US0007) or USB scanner (US0008) must pass through IHmacValidator before reaching the scan submission pipeline (EP0003). Constant-time comparison via `CryptographicOperations.FixedTimeEquals()` is mandatory to prevent timing-based side-channel attacks. Invalid payloads are never sent to the server.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Security | HMAC comparison via CryptographicOperations.FixedTimeEquals() | AC4 must verify constant-time comparison is used, not byte[].SequenceEqual() or == |
| PRD | Performance | Invalid QR rejection < 100ms | Validation must complete within 100ms including secret retrieval |
| TRD | Architecture | Service interfaces for all business logic; DI via MAUI container | IHmacValidator interface with concrete implementation registered in DI |
| TRD | Security | HMAC secret never logged or transmitted | AC must verify secret is not included in log output or rejection messages |
| TRD ADR-005 | Design Decision | Local HMAC validation before server submission | Service must return clear pass/fail; callers must not bypass validation |

---

## Acceptance Criteria

### AC1: QR Payload Parsing - Valid 4-Part Structure
- **Given** a QR payload string is received
- **When** the IHmacValidator.Validate() method is called with the payload
- **Then** the payload is split on the ":" delimiter expecting exactly 4 parts: prefix, studentId, timestamp, and hmacBase64
- **And** a payload with fewer than 4 parts returns a rejection result with reason "Malformed: expected 4 colon-separated parts, found {n}"
- **And** a payload with more than 4 parts returns a rejection result with reason "Malformed: expected 4 colon-separated parts, found {n}"

### AC2: SMARTLOG Prefix Verification
- **Given** a QR payload has been split into 4 parts
- **When** the first part is examined
- **Then** it must exactly equal "SMARTLOG" (case-sensitive)
- **And** a payload with prefix "smartlog", "SmartLog", "BADLOG", or any other value returns a rejection result with reason "InvalidPrefix: expected 'SMARTLOG'"

### AC3: HMAC-SHA256 Computation
- **Given** a QR payload with valid structure and correct SMARTLOG prefix
- **When** HMAC verification is performed
- **Then** the HMAC-SHA256 is computed over the string "{studentId}:{timestamp}" (parts[1] + ":" + parts[2]) using the shared secret retrieved from ISecureConfigService
- **And** the shared secret is obtained via ISecureConfigService.GetHmacSecretAsync()
- **And** the computed HMAC bytes are compared against the base64-decoded bytes from the payload's 4th part (hmacBase64)

### AC4: Constant-Time Signature Comparison
- **Given** the computed HMAC bytes and the decoded payload HMAC bytes are available
- **When** the two byte arrays are compared
- **Then** the comparison uses `CryptographicOperations.FixedTimeEquals()` exclusively
- **And** no other comparison method (SequenceEqual, ==, manual loop with early exit) is used
- **And** if the byte arrays have different lengths, the comparison still uses FixedTimeEquals (it returns false for different-length arrays)

### AC5: Valid QR Returns Success Result
- **Given** a QR payload with correct SMARTLOG prefix and valid HMAC signature
- **When** validation completes
- **Then** the method returns a success result containing the parsed studentId (string) and timestamp (string)
- **And** the result indicates IsValid = true
- **And** the result contains no rejection reason

### AC6: Invalid QR Returns Typed Rejection
- **Given** a QR payload that fails any validation step
- **When** validation completes
- **Then** the method returns a failure result with IsValid = false
- **And** the result contains a human-readable RejectionReason string
- **And** the rejection reason identifies the specific failure type: "Malformed", "InvalidPrefix", "InvalidSignature", "InvalidBase64", or "SecretUnavailable"
- **And** the rejection reason never includes the HMAC secret or any computed hash values

### AC7: Invalid QR Data Never Sent to Server
- **Given** a QR payload that fails HMAC validation
- **When** the validation result is consumed by the calling service
- **Then** no HTTP request is made to the SmartLog server for that payload
- **And** the service contract (IHmacValidator) returns a result type that forces callers to check IsValid before proceeding

### AC8: HMAC Secret Retrieval from SecureStorage
- **Given** the validation service needs the HMAC shared secret
- **When** ISecureConfigService.GetHmacSecretAsync() is called
- **Then** the secret is retrieved from MAUI SecureStorage (configured during EP0001 device setup)
- **And** if the secret is null or empty (not yet configured), validation returns a failure result with reason "SecretUnavailable: HMAC secret not configured"

---

## Scope

### In Scope
- IHmacValidator interface definition with Validate method
- HmacValidator concrete implementation
- HmacValidationResult record/class (IsValid, StudentId, Timestamp, RejectionReason)
- QR payload parsing (split on ":", count validation, prefix check)
- HMAC-SHA256 computation using System.Security.Cryptography.HMACSHA256
- Constant-time comparison via CryptographicOperations.FixedTimeEquals()
- Integration with ISecureConfigService for secret retrieval
- DI registration in MauiProgram.cs
- Comprehensive unit tests (TDD)

### Out of Scope
- QR code timestamp expiry validation (open question in EP0002 - pending Product decision)
- QR code generation or encoding
- Camera or USB scanner integration (handled by US0007 and US0008)
- Server-side validation or API communication
- HMAC secret key provisioning (handled by EP0001 setup wizard)
- Caching of the HMAC secret (implementation may cache, but not a requirement of this story)
- Student name lookup from studentId

---

## Technical Notes

### Service Interface

```csharp
public interface IHmacValidator
{
    Task<HmacValidationResult> ValidateAsync(string? qrPayload);
}
```

### Result Type

```csharp
public record HmacValidationResult(
    bool IsValid,
    string? StudentId = null,
    string? Timestamp = null,
    string? RejectionReason = null
);
```

### Implementation Approach
- Use `System.Security.Cryptography.HMACSHA256` for HMAC computation
- Use `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals()` for comparison
- Use `Convert.FromBase64String()` for decoding the HMAC from the payload (catch FormatException for invalid base64)
- The shared secret encoding (UTF-8 string to bytes, or base64-decoded) depends on the format agreed with the admin panel (see Open Questions)
- Register as `services.AddSingleton<IHmacValidator, HmacValidator>()` in MauiProgram.cs

### Key Classes
- `IHmacValidator` - service interface in `lib/core/services/`
- `HmacValidator` - implementation in `lib/core/services/`
- `HmacValidationResult` - result model in `lib/core/models/`
- Tests in `test/core/services/HmacValidatorTests.cs`

### Data Requirements
- **Input:** QR payload string (e.g., `SMARTLOG:STU-2026-001:1706918400:abc123hmacBase64==`)
- **Dependency:** HMAC shared secret from ISecureConfigService (stored in MAUI SecureStorage)
- **Output:** HmacValidationResult with parsed fields or rejection reason

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Null input | Return failure: "Malformed: payload is null" |
| Empty string input | Return failure: "Malformed: payload is empty" |
| Single value, no colons (e.g., "hello") | Return failure: "Malformed: expected 4 colon-separated parts, found 1" |
| Two colon-separated parts (e.g., "SMARTLOG:abc") | Return failure: "Malformed: expected 4 colon-separated parts, found 2" |
| Three colon-separated parts (e.g., "SMARTLOG:abc:123") | Return failure: "Malformed: expected 4 colon-separated parts, found 3" |
| Five or more colon-separated parts (e.g., "SMARTLOG:a:b:c:d") | Return failure: "Malformed: expected 4 colon-separated parts, found 5" |
| Wrong prefix (e.g., "BADLOG:STU-001:123:hmac==") | Return failure: "InvalidPrefix: expected 'SMARTLOG'" |
| Case-different prefix (e.g., "smartlog:STU-001:123:hmac==") | Return failure: "InvalidPrefix: expected 'SMARTLOG'" |
| Invalid base64 in HMAC part (e.g., "SMARTLOG:STU-001:123:!!!notbase64") | Return failure: "InvalidBase64: HMAC signature is not valid base64" |
| Valid format but incorrect HMAC (wrong signature) | Return failure: "InvalidSignature: HMAC verification failed" |
| Empty studentId (e.g., "SMARTLOG::123:hmac==") | Return failure: "Malformed: studentId is empty" |
| Empty timestamp (e.g., "SMARTLOG:STU-001::hmac==") | Return failure: "Malformed: timestamp is empty" |
| Non-numeric timestamp (e.g., "SMARTLOG:STU-001:notanumber:hmac==") | Still processed for HMAC (timestamp format validation is not in scope for this story; HMAC will fail if tampered) |
| Very long payload (> 10KB) | Process normally; no artificial length limit (HMAC computation handles arbitrary lengths) |
| HMAC secret not available in SecureStorage | Return failure: "SecretUnavailable: HMAC secret not configured" |
| StudentId containing special characters (e.g., "STU-2026/001") | Process normally; HMAC computed over literal string value |
| Whitespace-only payload (e.g., "   ") | Return failure: "Malformed: payload is empty" (after trim) |
| Payload with leading/trailing whitespace | Trim before processing; then validate normally |

---

## Test Scenarios

- [ ] Valid QR payload with correct HMAC returns IsValid=true with parsed studentId and timestamp
- [ ] Null payload returns IsValid=false with "Malformed: payload is null"
- [ ] Empty string payload returns IsValid=false with "Malformed: payload is empty"
- [ ] Whitespace-only payload returns IsValid=false with "Malformed: payload is empty"
- [ ] Payload with 1 part (no colons) returns Malformed rejection with part count
- [ ] Payload with 2 colon-separated parts returns Malformed rejection with part count
- [ ] Payload with 3 colon-separated parts returns Malformed rejection with part count
- [ ] Payload with 5+ colon-separated parts returns Malformed rejection with part count
- [ ] Payload with "BADLOG" prefix returns InvalidPrefix rejection
- [ ] Payload with "smartlog" (lowercase) prefix returns InvalidPrefix rejection
- [ ] Payload with invalid base64 in HMAC field returns InvalidBase64 rejection
- [ ] Payload with valid format but wrong HMAC signature returns InvalidSignature rejection
- [ ] Payload with empty studentId field returns Malformed rejection
- [ ] Payload with empty timestamp field returns Malformed rejection
- [ ] HMAC secret unavailable from ISecureConfigService returns SecretUnavailable rejection
- [ ] Valid payload: HMAC computed over "{studentId}:{timestamp}" (verify exact input to HMACSHA256)
- [ ] CryptographicOperations.FixedTimeEquals() is called for comparison (verify via code review or integration test)
- [ ] Rejection reasons never contain the HMAC secret value
- [ ] Payload with leading/trailing whitespace is trimmed before validation
- [ ] Multiple sequential calls with different payloads each return independent results
- [ ] Valid QR with special characters in studentId (e.g., "STU-2026/001") validates correctly when HMAC matches

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0001 | Blocked By | ISecureConfigService interface and GetHmacSecretAsync() method for retrieving the HMAC shared secret from SecureStorage | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| System.Security.Cryptography (HMACSHA256, CryptographicOperations) | .NET 8 BCL | Available |
| HMAC secret key format from admin panel (raw string, hex, or base64) | Backend team decision | Open Question |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

**Rationale:** The cryptographic operations are well-supported by .NET BCL. Complexity comes from thorough edge case handling (13+ scenarios), clean result type design, and security considerations (constant-time comparison, secret handling). No UI work. Heavy unit testing required (TDD).

---

## Open Questions

- [ ] What encoding is the HMAC secret key in when retrieved from SecureStorage? Is it stored as a raw UTF-8 string, hex-encoded, or base64-encoded? This affects how we convert the secret to bytes for HMACSHA256. - Owner: Backend Team
- [ ] Should empty studentId or empty timestamp be rejected at the parser level, or should we let the HMAC computation proceed (it will fail anyway if the QR was tampered)? Current decision: reject early with clear message. - Owner: Tech Lead
- [ ] Should the validator trim the payload before processing, or should callers be responsible for trimming? Current decision: validator trims. - Owner: Tech Lead

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
