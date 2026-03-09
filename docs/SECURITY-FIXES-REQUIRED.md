# Security Fixes Required - Priority List

**Generated:** 2026-02-16
**Status:** 🔴 BLOCKING PRODUCTION DEPLOYMENT

---

## 🔥 CRITICAL - Fix Immediately (Before ANY Deployment)

### 1. Remove Secrets from config.json
**File:** `SmartLog.Scanner.Core/Services/FileConfigService.cs`
**Priority:** P0 - CRITICAL
**Effort:** 4 hours

**Current Code:**
```csharp
public class AppConfig
{
    public string ApiKey { get; set; } = string.Empty;      // ❌ UNENCRYPTED
    public string HmacSecret { get; set; } = string.Empty;  // ❌ UNENCRYPTED
}
```

**Fix:**
```csharp
public class AppConfig
{
    // REMOVED: ApiKey and HmacSecret
    // These MUST ONLY be stored in SecureStorage (Keychain/DPAPI)
    public string ServerUrl { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public bool SetupCompleted { get; set; }
}
```

**Additional Changes Needed:**
- Update `SetupViewModel` to save secrets to `SecureStorage` only
- Remove `ApiKey`/`HmacSecret` from all config load/save methods
- Add migration code to move existing secrets from config.json to SecureStorage
- Delete old config.json files during migration

---

### 2. Remove Insecure Preferences Fallback
**File:** `SmartLog.Scanner.Core/Services/SecureConfigService.cs`
**Priority:** P0 - CRITICAL
**Effort:** 2 hours

**Current Code:**
```csharp
catch (Exception ex)
{
    // ❌ Falls back to unencrypted Preferences
    return Preferences.Default.Get<string?>(ConfigKeys.ApiKey, null);
}
```

**Fix:**
```csharp
catch (Exception ex)
{
#if DEBUG
    _logger.LogError(ex, "⚠️ DEV ONLY: Falling back to insecure Preferences");
    return Preferences.Default.Get<string?>(ConfigKeys.ApiKey, null);
#else
    _logger.LogError(ex, "SecureStorage unavailable - cannot access credentials");
    throw new SecurityException(
        "Device does not support secure credential storage. " +
        "Cannot proceed without SecureStorage (Keychain/DPAPI).", ex);
#endif
}
```

---

## 🟠 HIGH - Fix Before Production

### 3. Enforce HTTPS-Only
**File:** `SmartLog.Scanner.Core/ViewModels/SetupViewModel.cs`
**Priority:** P1 - HIGH
**Effort:** 2 hours

**Add Validation:**
```csharp
public async Task<bool> TestConnectionAsync()
{
#if !DEBUG
    // Production: Enforce HTTPS
    if (!ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        ErrorMessage = "HTTPS is required for production. HTTP is not allowed.";
        return false;
    }
#else
    if (!ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        _logger.LogWarning("⚠️ Using HTTP - acceptable in development only");
    }
#endif

    // ... continue with connection test
}
```

---

### 4. Implement Proper Certificate Pinning
**File:** `SmartLog.Scanner/MauiProgram.cs`
**Priority:** P1 - HIGH
**Effort:** 4 hours

**Current Code:**
```csharp
handler.ServerCertificateCustomValidationCallback =
    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;  // ❌ DANGEROUS
```

**Fix:**
```csharp
handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
{
    // Accept valid CA-signed certificates
    if (errors == SslPolicyErrors.None)
        return true;

    if (!acceptSelfSigned)
        return false;

    // For self-signed: Validate specific certificate thumbprint
    var expectedThumbprint = config.GetValue<string>("Server:CertificateThumbprint");
    if (string.IsNullOrEmpty(expectedThumbprint))
    {
        Log.Error("Self-signed cert enabled but no thumbprint configured");
        return false;
    }

    // Verify exact certificate match
    if (cert?.Thumbprint != expectedThumbprint)
    {
        Log.Error("Certificate mismatch. Expected: {Expected}, Got: {Actual}",
            expectedThumbprint, cert?.Thumbprint);
        return false;
    }

    // Verify hostname
    var requestHost = message.RequestUri?.Host;
    if (!requestHost?.Equals(cert?.GetNameInfo(X509NameType.DnsName, false),
        StringComparison.OrdinalIgnoreCase) ?? true)
    {
        Log.Error("Certificate hostname mismatch");
        return false;
    }

    Log.Warning("Accepting pinned self-signed certificate: {Thumbprint}", cert?.Thumbprint);
    return true;
};
```

**Configuration Change:**
```json
// appsettings.json
{
  "Server": {
    "AcceptSelfSignedCerts": false,  // Default: reject self-signed
    "CertificateThumbprint": "",     // Required if AcceptSelfSignedCerts = true
    "TimeoutSeconds": 30
  }
}
```

---

### 5. Validate Server Responses
**File:** `SmartLog.Scanner.Core/Services/ScanApiService.cs`
**Priority:** P1 - HIGH
**Effort:** 3 hours

**Add Validation Method:**
```csharp
private ScanResult ValidateAndBuildScanResult(ServerResponse response, string qrPayload)
{
    const int MaxNameLength = 200;
    const int MaxGradeLength = 20;
    const int MaxSectionLength = 10;
    const int MaxMessageLength = 500;

    // Validate and truncate strings
    var studentName = response.StudentName?.Length > MaxNameLength
        ? response.StudentName.Substring(0, MaxNameLength)
        : response.StudentName;

    var message = response.Message?.Length > MaxMessageLength
        ? response.Message.Substring(0, MaxMessageLength)
        : response.Message;

    // Validate StudentId format
    if (!string.IsNullOrEmpty(response.StudentId) &&
        !Regex.IsMatch(response.StudentId, @"^[A-Z0-9\-]{1,50}$"))
    {
        _logger.LogWarning("Invalid StudentId format from server: {StudentId}",
            response.StudentId);
        response.StudentId = "INVALID-FORMAT";
    }

    return new ScanResult
    {
        RawPayload = qrPayload,
        StudentId = response.StudentId,
        StudentName = studentName,
        Grade = response.Grade?.Substring(0, Math.Min(response.Grade?.Length ?? 0, MaxGradeLength)),
        Section = response.Section?.Substring(0, Math.Min(response.Section?.Length ?? 0, MaxSectionLength)),
        Message = message,
        Status = ValidateStatus(response.Status)
    };
}

private ScanStatus ValidateStatus(string? status)
{
    return status?.ToUpperInvariant() switch
    {
        "ACCEPTED" => ScanStatus.Accepted,
        "DUPLICATE" => ScanStatus.Duplicate,
        "REJECTED" => ScanStatus.Rejected,
        _ => ScanStatus.Error
    };
}
```

---

## 🟡 MEDIUM - Fix Before Full Production

### 6. Redact PII from Logs
**Priority:** P2 - MEDIUM (Compliance)
**Effort:** 2 hours

**Create Helper Class:**
```csharp
// SmartLog.Scanner.Core/Logging/PiiRedactor.cs
public static class PiiRedactor
{
    public static string RedactStudentId(string? studentId)
    {
        if (string.IsNullOrEmpty(studentId) || studentId.Length < 8)
            return "***";

        return $"{studentId.Substring(0, 3)}***{studentId.Substring(studentId.Length - 2)}";
    }

    public static string RedactQrPayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return "***";

        var parts = payload.Split(':');
        if (parts.Length != 4)
            return "***";

        return $"SMARTLOG:{RedactStudentId(parts[1])}:***:***";
    }
}
```

**Update All Log Statements:**
```csharp
// Before:
_logger.LogInformation("Scan submitted: {QrPayload}", qrPayload);

// After:
_logger.LogInformation("Scan submitted: {QrPayload}",
    PiiRedactor.RedactQrPayload(qrPayload));
```

---

### 7. Encrypt SQLite Database
**Priority:** P2 - MEDIUM
**Effort:** 3 hours

**Add SQLCipher:**
```bash
dotnet add package Microsoft.EntityFrameworkCore.Sqlite.Core
dotnet add package SQLitePCLRaw.bundle_e_sqlcipher
```

**Update MauiProgram.cs:**
```csharp
// Generate encryption key from device-specific data
var encryptionKey = await GenerateDatabaseKeyAsync();

builder.Services.AddDbContextFactory<ScannerDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Password={encryptionKey}"));
```

---

## Implementation Checklist

### Pre-Implementation
- [ ] Create security-fixes branch
- [ ] Back up current code
- [ ] Document current config.json format for migration

### Critical Fixes (P0)
- [ ] Remove ApiKey/HmacSecret from AppConfig model
- [ ] Update SetupViewModel to use SecureStorage only
- [ ] Remove Preferences fallback (or make DEBUG-only)
- [ ] Create migration code for existing installations
- [ ] Test on macOS and Windows

### High Priority Fixes (P1)
- [ ] Add HTTPS enforcement in SetupViewModel
- [ ] Implement certificate pinning with thumbprint validation
- [ ] Add server response validation
- [ ] Add maximum string length checks
- [ ] Test with malicious server responses

### Medium Priority Fixes (P2)
- [ ] Create PiiRedactor utility class
- [ ] Update all log statements to use redaction
- [ ] Add SQLCipher to project
- [ ] Generate database encryption key
- [ ] Test database encryption

### Testing
- [ ] Unit tests for validation logic
- [ ] Integration tests for SecureStorage
- [ ] Penetration testing
- [ ] Certificate validation testing
- [ ] Log review for PII leakage

### Documentation
- [ ] Update deployment guide with security requirements
- [ ] Document certificate pinning setup process
- [ ] Create security configuration checklist
- [ ] Update IT admin manual

---

## Estimated Total Effort

| Priority | Tasks | Effort |
|----------|-------|--------|
| P0 (Critical) | 2 | 6 hours |
| P1 (High) | 3 | 9 hours |
| P2 (Medium) | 2 | 5 hours |
| **TOTAL** | **7 tasks** | **~20 hours (2.5 days)** |

---

## Testing Plan

### Security Testing
1. **Secrets Storage Test**
   - Verify secrets NOT in config.json
   - Verify secrets in Keychain/DPAPI
   - Test app restart (secrets persist)

2. **HTTPS Enforcement Test**
   - Try HTTP URL in production build → Should fail
   - Try HTTP URL in debug build → Should warn

3. **Certificate Validation Test**
   - Valid CA cert → Accept
   - Self-signed without pinning → Reject
   - Self-signed with correct pin → Accept
   - Self-signed with wrong pin → Reject
   - Expired cert → Reject

4. **Input Validation Test**
   - Send 10MB student name → Should truncate
   - Send XSS payload in message → Should sanitize
   - Send invalid StudentId format → Should mark as invalid

---

## Sign-Off Required

Before deploying to production, obtain sign-off from:
- [ ] Security Team Lead
- [ ] IT Administrator
- [ ] Compliance Officer (if handling student data)
- [ ] Product Owner

---

**Priority:** 🔴 BLOCKING
**Target Completion:** 2-3 days
**Next Steps:** Create implementation branch and begin P0 fixes
