# SmartLog Project - Complete Context Document

**Version:** 1.1.0
**Last Updated:** 2026-02-16
**Status:** POC/Development - Ready for Testing

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [System Architecture](#system-architecture)
3. [SmartLog Scanner App](#smartlog-scanner-app)
4. [SmartLog Web App](#smartlog-web-app)
5. [Process Flow](#process-flow)
6. [API Contract](#api-contract)
7. [Security Model](#security-model)
8. [Data Flow](#data-flow)
9. [Current Status](#current-status)
10. [Key Concepts](#key-concepts)
11. [File Structure](#file-structure)
12. [Configuration](#configuration)
13. [Testing Setup](#testing-setup)
14. [Development Workflow](#development-workflow)

---

## Recent Updates (2026-02-16)

### ✅ Server Error Fix - COMPLETE
**Issue:** "Unexpected error occurred" when scanning QR codes
**Root Cause:** Mock server only supported GET /health, not POST /scans
**Fix:** Created new mock server with full POST support
- ✅ HMAC validation working with `test-hmac-qr-code-2026`
- ✅ Returns student names and details
- ✅ Duplicate detection (30-second window)
- ✅ All test QR codes regenerated
- 📄 See: `docs/server-error-fix.md`

### ✅ UI Theme Update - COMPLETE
**Change:** Updated from blue to teal/green theme
**Files Modified:**
- MainPage.xaml (8 color changes)
- MainViewModel.cs (3 color changes)
- SetupPage.xaml (8 color changes)
**Colors:**
- Dark Teal: #2C5F5D (headers, sidebar)
- Medium Teal: #4D9B91 (buttons, accents)
- Light Teal: #E0F2F1 (backgrounds)
- 📄 See: `docs/color-palette.md`

### ✅ EP0002 Implementation - COMPLETE
**Epic:** QR Code Scanning and Validation
**Status:** All 3 stories done (US0006, US0007, US0008)
- ✅ HMAC-SHA256 validation
- ✅ Camera QR scanning
- ✅ USB barcode scanner support
- 📄 See: `sdlc-studio/epics/EP0002-implementation-report.md`

### ✅ Testing Infrastructure - READY
- ✅ Mock server running (port 7001, PID 31723)
- ✅ 5 test QR codes with valid signatures
- ✅ Visual test page: `/tmp/test_qr_codes_new.html`
- ✅ Configuration script: `test-qr-codes/setup_test_config_updated.sh`
- ✅ App configured with correct HMAC secret
- 📄 See: `docs/testing-guide.md`

---

## Project Overview

### What is SmartLog?

SmartLog is a **school attendance tracking system** consisting of two applications:

1. **SmartLog Scanner** - Desktop app (.NET MAUI) for gate guards
2. **SmartLog Web App** - Admin portal (ASP.NET Core) for school administrators

### Business Problem

Schools need to track student entry/exit at gates during peak hours (hundreds of students in 30 minutes). Current manual processes are slow and error-prone.

### Solution

Students carry QR codes. Security guards scan these codes at gates. The system:
- ✅ Validates QR authenticity locally (HMAC signature)
- ✅ Records attendance in real-time
- ✅ Provides instant visual/audio feedback
- ✅ Works offline (queues scans when network unavailable)
- ✅ Prevents duplicate scans

---

## System Architecture

### High-Level Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    SCHOOL NETWORK (LAN)                     │
│                                                             │
│  ┌──────────────────┐                ┌──────────────────┐  │
│  │  SmartLog Web    │◄──── REST ────►│  SmartLog        │  │
│  │  App (Admin)     │      API       │  Scanner (Gate)  │  │
│  │                  │                │                  │  │
│  │  ASP.NET Core    │                │  .NET MAUI       │  │
│  │  Razor Pages     │                │  Desktop App     │  │
│  │  SQL Server      │                │  SQLite Queue    │  │
│  │                  │                │                  │  │
│  │  • Device Mgmt   │                │  • QR Scanning   │  │
│  │  • Student DB    │                │  • HMAC Check    │  │
│  │  • Reports       │                │  • Offline Queue │  │
│  │  • QR Generator  │                │  • Feedback UI   │  │
│  └──────────────────┘                └──────────────────┘  │
│         │                                     ▲             │
│         │                                     │             │
│         ▼                                     │             │
│  ┌──────────────────┐                        │             │
│  │  SQL Server      │                        │             │
│  │  Database        │                        │             │
│  │                  │                        │             │
│  │  • Students      │                        │             │
│  │  • Scans         │                USB/Camera            │
│  │  • Devices       │                        │             │
│  │  • Audit Logs    │                        │             │
│  └──────────────────┘                        │             │
│                                               │             │
└───────────────────────────────────────────────┼─────────────┘
                                                │
                                        ┌───────▼────────┐
                                        │  Student QR    │
                                        │  (Printed)     │
                                        │                │
                                        │  SMARTLOG:     │
                                        │  {id}:{ts}:    │
                                        │  {hmac}        │
                                        └────────────────┘
```

### Component Interaction

1. **Admin Portal** (SmartLog Web App):
   - Generates QR codes with HMAC signatures
   - Manages student database
   - Registers scanner devices and issues API keys
   - Views attendance reports
   - Configures HMAC shared secret

2. **Scanner App** (SmartLog Scanner):
   - Scans student QR codes (camera or USB scanner)
   - Validates HMAC locally (no server needed for validation)
   - Submits to server API
   - Queues offline if network unavailable
   - Provides instant visual/audio feedback

3. **Communication**:
   - REST API over HTTP/HTTPS (LAN)
   - Scanner → Web App (scan submissions)
   - Web App → Scanner (device registration, configuration)

---

## SmartLog Scanner App

### Purpose

Desktop application for security guards to scan student QR codes at school gates.

### Technology Stack

| Component | Technology |
|-----------|-----------|
| **Framework** | .NET MAUI (Multi-platform App UI) |
| **Language** | C# 12 |
| **Runtime** | .NET 8.0 LTS |
| **Platforms** | macOS (MacCatalyst), Windows (WinUI 3) |
| **Architecture** | MVVM with layered services |
| **DI Container** | Microsoft.Extensions.DependencyInjection |
| **Database** | SQLite (offline queue) via EF Core |
| **HTTP Client** | HttpClient with Polly (retry, circuit breaker) |
| **QR Scanning** | Camera: ZXing.Net.Maui / USB: Keyboard wedge |
| **Logging** | Serilog (file + console) |
| **Audio** | Plugin.Maui.Audio |
| **Secure Storage** | MAUI SecureStorage (Keychain/DPAPI) |

### Key Features

1. **QR Code Scanning**
   - Camera mode: Live preview with continuous scanning
   - USB mode: Keyboard wedge input (barcode scanner)
   - Debounce: 500ms raw payload + 30s student-level

2. **HMAC Validation**
   - Local signature verification before server contact
   - Constant-time comparison (prevents timing attacks)
   - Rejects forged/tampered QR codes instantly

3. **Visual Feedback**
   - ✅ Green: Accepted
   - ⚠️ Amber: Duplicate/Warning
   - ❌ Red: Rejected/Error
   - ℹ️ Teal: Info/Queued
   - Auto-clear after 3 seconds

4. **Audio Feedback**
   - success.wav (Accepted)
   - duplicate.wav (Duplicate)
   - error.wav (Rejected)
   - Plays automatically on scan result

5. **Smart Deduplication**
   - 0-2s: Silent suppression (camera jitter)
   - 2-30s: Amber warning (already scanned)
   - 30s+: Allow (server decides)
   - ENTRY/EXIT tracked independently

6. **Offline Resilience** (Currently Disabled)
   - SQLite queue for scans when offline
   - Auto-sync when connectivity restored
   - Exponential backoff retry logic

7. **Health Monitoring**
   - 15-second server polling
   - Stability window (2 consecutive checks)
   - Visual connectivity indicator

### Project Structure

```
SmartLogScannerApp/
├── SmartLog.Scanner/                    # Main MAUI app
│   ├── Views/
│   │   ├── MainPage.xaml               # Main scanning UI
│   │   ├── SetupPage.xaml              # Configuration UI
│   │   └── Controls/
│   │       └── CameraQrView.cs         # Camera preview
│   ├── ViewModels/
│   │   └── MainViewModel.cs            # Main page logic
│   ├── Platforms/
│   │   └── MacCatalyst/
│   │       └── CameraQrScannerView.cs  # Native camera
│   └── MauiProgram.cs                  # DI, services, config
│
├── SmartLog.Scanner.Core/              # Business logic
│   ├── Services/
│   │   ├── HmacValidator.cs            # HMAC-SHA256 validation
│   │   ├── CameraQrScannerService.cs   # Camera scanning
│   │   ├── UsbQrScannerService.cs      # USB scanner
│   │   ├── ScanApiService.cs           # Server API client
│   │   ├── OfflineQueueService.cs      # SQLite queue
│   │   ├── HealthCheckService.cs       # Connectivity monitor
│   │   ├── ScanDeduplicationService.cs # Duplicate detection
│   │   ├── FileConfigService.cs        # Config file mgmt
│   │   └── SecureConfigService.cs      # Keychain/DPAPI
│   ├── Models/
│   │   ├── ScanResult.cs               # Scan response model
│   │   ├── HmacValidationResult.cs     # Validation result
│   │   └── QueuedScan.cs               # Offline queue entity
│   └── Data/
│       └── ScannerDbContext.cs         # EF Core context
│
├── SmartLog.Scanner.Tests/             # Unit tests
│   └── Services/
│       ├── HmacValidatorTests.cs
│       └── UsbQrScannerServiceTests.cs
│
├── sdlc-studio/                        # SDLC documentation
│   ├── prd.md                          # Product requirements
│   ├── trd.md                          # Technical requirements
│   ├── epics/                          # Epic documents
│   └── stories/                        # User stories
│
├── test-qr-codes/                      # Test QR codes
│   ├── test_qr_codes.html              # Visual QR codes
│   ├── generate_test_qr.py             # QR generator
│   └── setup_test_config.sh            # Config script
│
└── docs/                               # Documentation
    ├── color-palette.md                # UI theme colors
    ├── feature-gap-analysis.md         # Implementation status
    └── server-error-fix.md             # Troubleshooting
```

### Location

```
/Users/markmarmeto/Projects/SmartLogScannerApp/
```

---

## SmartLog Web App

### Purpose

Web-based admin portal for school administrators to manage students, devices, and view attendance reports.

### Technology Stack

| Component | Technology |
|-----------|-----------|
| **Framework** | ASP.NET Core 8.0 |
| **UI** | Razor Pages |
| **Language** | C# 12 |
| **Database** | SQL Server |
| **ORM** | Entity Framework Core |
| **Authentication** | ASP.NET Core Identity |
| **API** | REST API with minimal APIs |
| **Hosting** | IIS / Kestrel (self-hosted on LAN) |

### Key Features

1. **Student Management**
   - Add/edit/deactivate students
   - Grade levels and sections
   - Photo management

2. **QR Code Generation**
   - Generates SMARTLOG:{id}:{timestamp}:{hmac}
   - Uses shared HMAC secret
   - Printable ID cards

3. **Device Registration**
   - Register scanner devices
   - Issue API keys
   - Configure scan modes

4. **Attendance Reports**
   - Daily/weekly/monthly reports
   - Entry/exit tracking
   - Export to CSV/Excel

5. **System Configuration**
   - HMAC shared secret management
   - School settings
   - Audit logs

### Project Structure

```
SmartLogWebApp/
├── Pages/
│   ├── Students/
│   │   ├── Index.cshtml              # Student list
│   │   ├── Create.cshtml             # Add student
│   │   └── QRCode.cshtml             # Generate QR
│   ├── Devices/
│   │   ├── Index.cshtml              # Device list
│   │   └── Register.cshtml           # Register device
│   └── Reports/
│       └── Attendance.cshtml         # Attendance reports
│
├── Api/
│   ├── HealthEndpoint.cs             # GET /api/v1/health
│   └── ScanEndpoint.cs               # POST /api/v1/scans
│
├── Models/
│   ├── Student.cs                    # Student entity
│   ├── Device.cs                     # Device entity
│   ├── Scan.cs                       # Scan record entity
│   └── ScanRequest.cs                # API request model
│
├── Services/
│   ├── QrCodeGenerator.cs            # QR generation
│   ├── HmacService.cs                # HMAC calculation
│   └── ScanProcessor.cs              # Scan validation
│
└── Data/
    └── ApplicationDbContext.cs       # EF Core context
```

### Location

```
/Users/markmarmeto/Projects/SmartLogWebApp/
```

### Database Schema

```sql
-- Students table
CREATE TABLE Students (
    StudentId NVARCHAR(50) PRIMARY KEY,
    FirstName NVARCHAR(100) NOT NULL,
    LastName NVARCHAR(100) NOT NULL,
    Grade NVARCHAR(20),
    Section NVARCHAR(10),
    Active BIT DEFAULT 1,
    PhotoUrl NVARCHAR(500),
    CreatedAt DATETIME2 DEFAULT GETUTCDATE()
);

-- Devices table
CREATE TABLE Devices (
    DeviceId UNIQUEIDENTIFIER PRIMARY KEY,
    DeviceName NVARCHAR(100),
    ApiKey NVARCHAR(100) UNIQUE,
    ScannerMode NVARCHAR(20), -- 'Camera' or 'USB'
    Active BIT DEFAULT 1,
    RegisteredAt DATETIME2,
    LastSeenAt DATETIME2
);

-- Scans table
CREATE TABLE Scans (
    ScanId UNIQUEIDENTIFIER PRIMARY KEY,
    StudentId NVARCHAR(50) REFERENCES Students(StudentId),
    DeviceId UNIQUEIDENTIFIER REFERENCES Devices(DeviceId),
    ScanType NVARCHAR(10), -- 'ENTRY' or 'EXIT'
    ScannedAt DATETIME2,
    Status NVARCHAR(20), -- 'ACCEPTED', 'DUPLICATE'
    CreatedAt DATETIME2 DEFAULT GETUTCDATE()
);

-- System Config
CREATE TABLE SystemConfig (
    ConfigKey NVARCHAR(100) PRIMARY KEY,
    ConfigValue NVARCHAR(MAX),
    UpdatedAt DATETIME2
);
-- Stores: 'HMAC_SECRET', 'SCHOOL_NAME', etc.
```

---

## Process Flow

### 1. Initial Setup (IT Admin - One Time)

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Web App: Admin sets HMAC shared secret                  │
│    → Stored in SystemConfig table                          │
│                                                             │
│ 2. Web App: Admin registers scanner device                 │
│    → Generates API key                                     │
│    → Stores in Devices table                               │
│                                                             │
│ 3. Scanner App: IT Admin runs setup wizard                 │
│    → Enters server URL (http://192.168.1.100:8443)        │
│    → Pastes API key from web app                          │
│    → Enters HMAC shared secret (same as web app)          │
│    → Saves to SecureStorage                               │
│                                                             │
│ 4. Web App: Admin adds students                            │
│    → Student records created in database                   │
│    → QR codes generated with HMAC signature                │
│    → ID cards printed and distributed to students          │
└─────────────────────────────────────────────────────────────┘
```

### 2. Daily Operation (Security Guard)

```
┌─────────────────────────────────────────────────────────────┐
│ MORNING (ENTRY)                                             │
│                                                             │
│ 1. Guard launches Scanner App                              │
│    → App starts in ENTRY mode (default)                   │
│    → Camera preview active (or USB ready)                 │
│                                                             │
│ 2. Student shows QR code                                    │
│    ┌──────────────────────┐                                │
│    │ QR Code:             │                                │
│    │ SMARTLOG:            │                                │
│    │ STU-2026-001:        │                                │
│    │ 1773032563:          │                                │
│    │ +HRvXV3FLt6N...      │                                │
│    └──────────────────────┘                                │
│                                                             │
│ 3. Scanner App processes QR                                 │
│    a. Camera decodes QR payload                            │
│    b. HmacValidator.ValidateAsync()                        │
│       • Split by ':'                                       │
│       • Check prefix = 'SMARTLOG'                          │
│       • Compute HMAC-SHA256({id}:{ts})                    │
│       • FixedTimeEquals(computed, received)               │
│    c. If invalid → RED "Invalid QR code"                  │
│    d. If valid → Continue...                              │
│                                                             │
│ 4. Student-level deduplication                             │
│    • Check: STU-2026-001 + ENTRY scanned < 30s ago?       │
│    • If < 2s → Silent suppression (return)                │
│    • If < 30s → AMBER "Already scanned"                   │
│    • If > 30s → Continue to server...                     │
│                                                             │
│ 5. Submit to server (if online)                            │
│    POST /api/v1/scans                                      │
│    Headers: X-API-Key: {deviceApiKey}                     │
│    Body: {                                                 │
│      "qrPayload": "SMARTLOG:...",                         │
│      "scannedAt": "2026-03-09T07:30:00Z",                │
│      "scanType": "ENTRY"                                  │
│    }                                                       │
│                                                             │
│ 6. Server processes scan (Web App)                         │
│    a. Validate API key                                     │
│    b. Parse QR payload                                     │
│    c. Re-verify HMAC (double check)                       │
│    d. Lookup student in database                          │
│    e. Check for duplicate (same student+type today)       │
│    f. Insert scan record                                  │
│    g. Return response                                      │
│                                                             │
│ 7. Scanner App receives response                           │
│    {                                                       │
│      "scanId": "SCAN-STU-2026-001-1773003793",          │
│      "studentId": "STU-2026-001",                        │
│      "studentName": "Juan Dela Cruz",                    │
│      "grade": "Grade 11",                                │
│      "section": "A",                                     │
│      "scanType": "ENTRY",                                │
│      "status": "ACCEPTED",                               │
│      "message": "Welcome, Juan Dela Cruz!"               │
│    }                                                       │
│                                                             │
│ 8. Display feedback                                        │
│    ┌─────────────────────────────────────┐                │
│    │  ✓ Juan Dela Cruz - Grade 11 A     │  ← GREEN       │
│    │  🕒 07:30:45            ENTRY       │                │
│    └─────────────────────────────────────┘                │
│    🔊 Play success.wav                                    │
│    ⏱️ Auto-clear after 3 seconds                          │
│                                                             │
│ 9. Statistics updated                                      │
│    Footer: "Today: 127 scans"                             │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ AFTERNOON (EXIT)                                            │
│                                                             │
│ 1. Guard clicks toggle: ENTRY → EXIT                      │
│    → App switches to EXIT mode                            │
│    → Scan type changes to "EXIT"                          │
│                                                             │
│ 2-9. Same process as ENTRY, but with scanType="EXIT"      │
│      → Deduplication tracked separately per type          │
│      → Can scan same student for EXIT after ENTRY         │
└─────────────────────────────────────────────────────────────┘
```

### 3. Offline Scenario (Currently Disabled)

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Network goes down                                        │
│    → HealthCheckService detects (15s polling)              │
│    → Status changes: 🟢 Online → 🔴 Offline                │
│                                                             │
│ 2. Student scans QR                                         │
│    → HMAC validates locally (no server needed)             │
│    → If valid, saves to SQLite queue                       │
│    → BLUE "Scan queued (offline)"                          │
│    → Queue count increments                                │
│                                                             │
│ 3. Network restored                                         │
│    → HealthCheckService detects                            │
│    → BackgroundSyncService triggers                        │
│    → Processes queue FIFO (oldest first)                   │
│    → Submits to server                                     │
│    → Updates queue count                                   │
│                                                             │
│ NOTE: Offline mode currently disabled per user request     │
│       App shows error instead of queueing                  │
└─────────────────────────────────────────────────────────────┘
```

---

## API Contract

### REST API Endpoints

#### 1. Health Check

**Endpoint:** `GET /api/v1/health`

**Purpose:** Server health and connectivity check

**Request:**
```http
GET /api/v1/health HTTP/1.1
Host: 192.168.1.100:8443
```

**Response (200 OK):**
```json
{
  "status": "healthy",
  "timestamp": "2026-03-09T05:03:12Z",
  "version": "1.0.0"
}
```

**Scanner Usage:**
- Polled every 15 seconds
- No authentication required
- Determines online/offline status

---

#### 2. Submit Scan

**Endpoint:** `POST /api/v1/scans`

**Purpose:** Submit validated QR scan to server

**Request:**
```http
POST /api/v1/scans HTTP/1.1
Host: 192.168.1.100:8443
Content-Type: application/json
X-API-Key: test-api-key-12345

{
  "qrPayload": "SMARTLOG:STU-2026-001:1773032563:+HRvXV3FLt6N...",
  "scannedAt": "2026-03-09T07:30:45Z",
  "scanType": "ENTRY"
}
```

**Response (200 OK - ACCEPTED):**
```json
{
  "scanId": "SCAN-STU-2026-001-1773003793",
  "studentId": "STU-2026-001",
  "studentName": "Juan Dela Cruz",
  "grade": "Grade 11",
  "section": "A",
  "scanType": "ENTRY",
  "scannedAt": "2026-03-09T07:30:45Z",
  "status": "ACCEPTED",
  "message": "Welcome, Juan Dela Cruz!"
}
```

**Response (200 OK - DUPLICATE):**
```json
{
  "scanId": "SCAN-STU-2026-001-1773004123",
  "studentId": "STU-2026-001",
  "studentName": "Juan Dela Cruz",
  "grade": "Grade 11",
  "section": "A",
  "scanType": "ENTRY",
  "scannedAt": "2026-03-09T07:35:30Z",
  "status": "DUPLICATE",
  "message": "Juan Dela Cruz already scanned. Please proceed.",
  "originalScanId": "SCAN-STU-2026-001-1773003793"
}
```

**Response (400 Bad Request - REJECTED):**
```json
{
  "error": "InvalidSignature",
  "message": "HMAC signature verification failed",
  "status": "REJECTED"
}
```

**Response (401 Unauthorized):**
```json
{
  "error": "Unauthorized",
  "message": "Invalid API key"
}
```

**Response (429 Too Many Requests):**
```http
HTTP/1.1 429 Too Many Requests
Retry-After: 60

{
  "error": "RateLimitExceeded",
  "message": "Maximum 60 scans per minute exceeded"
}
```

---

## Security Model

### HMAC-SHA256 Signature

**Purpose:** Prevent forged/tampered QR codes

**Format:**
```
SMARTLOG:{studentId}:{timestamp}:{hmacBase64}
```

**Generation (Web App):**
```csharp
// 1. Get shared secret from config
string secret = config.HmacSecret; // e.g., "test-hmac-qr-code-2026"

// 2. Create message from student ID and timestamp
string studentId = "STU-2026-001";
string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
string message = $"{studentId}:{timestamp}";

// 3. Compute HMAC-SHA256
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
string hmacBase64 = Convert.ToBase64String(hash);

// 4. Construct QR payload
string qrPayload = $"SMARTLOG:{studentId}:{timestamp}:{hmacBase64}";
```

**Validation (Scanner App):**
```csharp
// 1. Parse QR payload
string[] parts = qrPayload.Split(':');
if (parts.Length != 4 || parts[0] != "SMARTLOG")
    return Invalid;

string studentId = parts[1];
string timestamp = parts[2];
string receivedHmac = parts[3];

// 2. Recompute HMAC using same secret
string message = $"{studentId}:{timestamp}";
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
byte[] expectedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
byte[] receivedHash = Convert.FromBase64String(receivedHmac);

// 3. Constant-time comparison (prevents timing attacks)
bool valid = CryptographicOperations.FixedTimeEquals(expectedHash, receivedHash);
```

**Security Properties:**
- ✅ Cannot forge QR without secret
- ✅ Cannot modify student ID without invalidating HMAC
- ✅ Constant-time comparison prevents timing attacks
- ✅ Validated locally (no server needed)
- ✅ Timestamp prevents replay (though not enforced in current POC)

### API Key Authentication

**Device Registration:**
1. Admin registers device in Web App
2. Web App generates unique API key (GUID)
3. IT Admin enters API key in Scanner setup
4. Scanner includes key in `X-API-Key` header

**Validation:**
- Server checks `X-API-Key` against Devices table
- 401 Unauthorized if missing/invalid
- Rate limiting per device (60 scans/minute)

### Secure Storage

**Scanner App (MAUI SecureStorage):**
- macOS: Keychain
- Windows: DPAPI (Data Protection API)

**Stored Securely:**
- API Key
- HMAC Secret

**Stored in Preferences (not encrypted):**
- Server URL
- Scanner mode (Camera/USB)
- Default scan type (ENTRY/EXIT)

---

## Data Flow

### QR Code Lifecycle

```
┌─────────────────────────────────────────────────────────────┐
│ 1. WEB APP: Generate QR Code                               │
│                                                             │
│    Student: Juan Dela Cruz (STU-2026-001)                  │
│    Timestamp: 1773032563                                   │
│    Secret: test-hmac-qr-code-2026                          │
│                                                             │
│    HMAC = SHA256("STU-2026-001:1773032563", secret)        │
│         = +HRvXV3FLt6NsJlXuBH6DRRc4CI3ZfhJXHfg+3+uRFY=    │
│                                                             │
│    QR Payload:                                             │
│    SMARTLOG:STU-2026-001:1773032563:+HRvXV3FLt6N...       │
│                                                             │
│    → Print ID card                                         │
│    → Distribute to student                                 │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ 2. STUDENT: Carries QR Code                                │
│    (Permanent - no expiry in current POC)                  │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ 3. SCANNER APP: Scan QR Code                               │
│                                                             │
│    a. Camera/USB captures QR                               │
│    b. Decode: "SMARTLOG:STU-2026-001:1773032563:+HRvX..."  │
│    c. HMAC validation (local)                              │
│       • Recompute HMAC with shared secret                  │
│       • Compare with received HMAC                         │
│       • ✅ Valid → Continue                                │
│       • ❌ Invalid → Reject (no server call)               │
│                                                             │
│    d. Deduplication check (local)                          │
│       • STU-2026-001 + ENTRY scanned < 30s ago?           │
│       • If yes → Amber warning (no server call)           │
│       • If no → Continue to server                        │
│                                                             │
│    e. HTTP POST to server                                  │
│       X-API-Key: test-api-key-12345                       │
│       Body: { qrPayload, scannedAt, scanType }            │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ 4. WEB APP: Process Scan                                   │
│                                                             │
│    a. Validate API key                                     │
│    b. Re-verify HMAC (server-side)                         │
│    c. Lookup student: STU-2026-001                         │
│       → Juan Dela Cruz, Grade 11 A                         │
│    d. Check duplicates (DB query)                          │
│       SELECT * FROM Scans                                  │
│       WHERE StudentId = 'STU-2026-001'                     │
│         AND ScanType = 'ENTRY'                             │
│         AND DATE(ScannedAt) = CURDATE()                    │
│    e. Insert scan record                                   │
│       INSERT INTO Scans (ScanId, StudentId, DeviceId,      │
│                          ScanType, ScannedAt, Status)      │
│    f. Return response with student details                 │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ 5. SCANNER APP: Display Result                             │
│                                                             │
│    Response: {                                             │
│      "status": "ACCEPTED",                                 │
│      "studentName": "Juan Dela Cruz",                      │
│      "grade": "Grade 11",                                  │
│      "section": "A"                                        │
│    }                                                       │
│                                                             │
│    Display: ✅ GREEN                                       │
│    "✓ Juan Dela Cruz - Grade 11 A"                        │
│    🔊 success.wav                                          │
│    ⏱️ Auto-clear 3s                                        │
└─────────────────────────────────────────────────────────────┘
```

---

## Current Status

### Epic Completion Status

| Epic | Title | Status | Stories |
|------|-------|--------|---------|
| EP0001 | Device Setup and Configuration | ✅ Done | US0001-US0005 (5/5) |
| EP0002 | QR Code Scanning and Validation | ✅ Done | US0006-US0008 (3/3) |
| EP0003 | Scan Processing and Feedback | 🚧 Next | US0009-US0013 (0/5) |
| EP0004 | Offline Resilience and Sync | ⏸️ Deferred | US0014-US0017 (0/4) |

**Overall Progress:** 8/17 stories complete (47%)

### What's Implemented ✅

**SmartLog Scanner App:**
- ✅ Camera QR scanning (ZXing.Net.Maui)
- ✅ USB keyboard wedge support
- ✅ HMAC-SHA256 validation (constant-time)
- ✅ Visual feedback (teal/green theme) ⬅️ NEW
- ✅ Audio feedback (4 sounds)
- ✅ Smart deduplication (3-tier windows)
- ✅ ENTRY/EXIT toggle
- ✅ Health check monitoring
- ✅ Statistics display
- ✅ Secure config storage
- ✅ Self-signed TLS support
- ✅ Settings UI (accessible via ⚙️)
- ✅ First-launch setup flow
- ✅ Global error handling
- ✅ Serilog logging
- ✅ Mock server with full API support ⬅️ NEW
- ✅ Test QR codes and testing infrastructure ⬅️ NEW

**Offline Features (Implemented but Disabled):**
- ⚠️ SQLite offline queue
- ⚠️ Background sync service
- ⚠️ Auto-retry with exponential backoff

**SmartLog Web App:**
- ❓ Status unknown (separate project)
- ❓ Assumed to have student management
- ❓ Assumed to have QR generation
- ❓ API endpoints defined but not verified

### What's NOT Implemented ❌

**Scanner App:**
- ❌ Scan history viewer UI
- ❌ Advanced statistics dashboard
- ❌ Custom audio file upload
- ❌ QR timestamp expiry validation
- ❌ Remote configuration
- ❌ Certificate pinning

**Web App:**
- ❌ Full implementation unknown (separate project)

---

## Key Concepts

### 1. HMAC Validation

**What:** Cryptographic signature to verify QR authenticity

**Why:** Prevent students from creating fake QR codes

**How:**
- Admin sets shared secret in both systems
- Web App signs QR: HMAC-SHA256({studentId}:{timestamp}, secret)
- Scanner validates: Recompute HMAC, compare with received
- Uses constant-time comparison to prevent timing attacks

**Result:** Forged QR codes rejected instantly without server

---

### 2. Deduplication

**Problem:** Camera held on same QR = dozens of scans per second

**Solution:** 3-tier time windows

| Window | Duration | Action | Reason |
|--------|----------|--------|--------|
| Suppress | 0-2s | Silent ignore | Camera jitter, USB double-tap |
| Warn | 2-30s | Amber feedback, no API call | Student still at gate |
| Server | 30s+ | Pass to server | Could be legitimate re-entry |

**Implementation:**
- Tracks: `{studentId}:{scanType}` → Last scan time
- ENTRY/EXIT tracked independently
- Cached student name shown in amber warning

---

### 3. Offline Queue (Disabled)

**What:** SQLite database stores scans when server unavailable

**Flow:**
1. Network down → Health check detects
2. Valid scan → Save to SQLite (status: PENDING)
3. Network up → Background sync triggers
4. Process queue FIFO → Submit to server
5. Update status: PENDING → SYNCED or FAILED

**Current Status:** Disabled per user request ("always-online mode")

---

### 4. Health Check

**What:** Periodic server connectivity monitoring

**Mechanism:**
- Poll GET /health every 15 seconds
- Dedicated HttpClient (no Polly retry)
- Stability window: 2 consecutive checks before status change
- Optimistic default: Assume online until proven offline

**UI Indicator:**
- 🟢 Online (green badge)
- 🔴 Offline (red badge)
- ⚪ Connecting (gray badge)

---

### 5. Rate Limiting

**Client-Side:**
- Sliding window: 60 scans/minute
- Tracks request timestamps
- Logs warning but continues (server enforces)

**Server-Side:**
- Returns 429 Too Many Requests
- Includes Retry-After header
- Scanner shows amber "Rate limit exceeded"

---

## File Structure

### Scanner App (SmartLogScannerApp)

```
SmartLogScannerApp/
├── SmartLog.Scanner.sln
├── SmartLog.Scanner/
│   ├── Platforms/
│   │   ├── MacCatalyst/
│   │   │   ├── Info.plist              # NSCameraUsageDescription
│   │   │   └── CameraQrScannerView.cs  # AVFoundation camera
│   │   └── Windows/
│   │       └── Package.appxmanifest    # Webcam capability
│   ├── Resources/
│   │   ├── Sounds/
│   │   │   ├── success.wav
│   │   │   ├── duplicate.wav
│   │   │   └── error.wav
│   │   └── Images/
│   ├── Views/
│   │   ├── MainPage.xaml
│   │   ├── MainPage.xaml.cs
│   │   ├── SetupPage.xaml
│   │   └── Controls/
│   ├── MauiProgram.cs                  # DI, Polly, HttpClient
│   ├── App.xaml.cs                     # Global handlers
│   └── AppShell.xaml                   # Navigation
│
├── SmartLog.Scanner.Core/
│   ├── Services/                       # Business logic
│   ├── Models/                         # DTOs, entities
│   ├── Data/                           # EF Core DbContext
│   ├── Constants/                      # Configs, enums
│   └── ViewModels/                     # MVVM ViewModels
│
├── SmartLog.Scanner.Tests/
│   └── Services/
│
├── sdlc-studio/                        # SDLC docs
│   ├── prd.md
│   ├── trd.md
│   ├── epics/
│   │   ├── EP0001-device-setup-and-configuration.md
│   │   ├── EP0002-qr-code-scanning-and-validation.md
│   │   ├── EP0003-scan-processing-and-feedback.md
│   │   └── EP0004-offline-resilience-and-sync.md
│   └── stories/
│       ├── US0001-secure-config-storage.md
│       ├── US0006-local-hmac-qr-validation.md
│       ├── US0007-camera-based-qr-scanning.md
│       └── ... (17 stories total)
│
├── test-qr-codes/
│   ├── test_qr_codes.html
│   ├── generate_test_qr.py
│   ├── setup_test_config.sh
│   └── README.md
│
└── docs/
    ├── color-palette.md
    ├── feature-gap-analysis.md
    ├── settings-navigation-fix.md
    └── server-error-fix.md
```

---

## Configuration

### Scanner App Configuration

**File Locations:**

1. **Secure Storage** (Keychain/DPAPI):
   ```
   macOS: ~/Library/Keychains/login.keychain-db
   Windows: DPAPI encrypted registry
   ```
   Stores:
   - `ApiKey`
   - `HmacSecret`

2. **File Config** (`config.json`):
   ```
   macOS: ~/Library/Containers/com.smartlog.scanner/Data/Library/Preferences/config.json
   Windows: %LOCALAPPDATA%\SmartLog\Scanner\config.json
   ```
   Stores:
   ```json
   {
     "ServerUrl": "http://localhost:7001",
     "ApiKey": "test-api-key-12345",
     "HmacSecret": "test-hmac-qr-code-2026",
     "DeviceId": "TEST-SCANNER-001",
     "DeviceName": "Test Scanner - POC",
     "SetupCompleted": true,
     "Scanner": {
       "Mode": "Camera",
       "DefaultScanType": "ENTRY"
     }
   }
   ```

3. **Preferences** (MAUI Preferences):
   - `Setup.Completed` (bool)
   - `Scanner.Mode` (string: "Camera" or "USB")
   - `Scanner.DefaultScanType` (string: "ENTRY" or "EXIT")

**Configuration via UI:**
1. Click ⚙️ Settings button
2. Enter values on SetupPage
3. Click "Test Connection"
4. Click "Save Configuration"

---

### Web App Configuration

**appsettings.json:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=SmartLogDB;..."
  },
  "HmacSecret": "test-hmac-qr-code-2026",
  "RateLimit": {
    "MaxScansPerMinute": 60
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:7001"
      },
      "Https": {
        "Url": "https://localhost:8443",
        "Certificate": {
          "Path": "cert.pfx",
          "Password": "password"
        }
      }
    }
  }
}
```

---

## Testing Setup

### Mock Server

**Location:** `/tmp/mock-smartlog-server-updated.py`

**Features:**
- GET /api/v1/health → Health check
- POST /api/v1/scans → Scan submission
- HMAC validation with `test-hmac-qr-code-2026`
- Returns student names and details
- Duplicate detection (30s window)

**Start:**
```bash
python3 /tmp/mock-smartlog-server-updated.py &
```

**Stop:**
```bash
kill $(cat /tmp/server.pid)
```

**Test:**
```bash
# Health check
curl http://localhost:7001/api/v1/health

# Submit scan
curl -X POST http://localhost:7001/api/v1/scans \
  -H "Content-Type: application/json" \
  -H "X-API-Key: test-api-key-12345" \
  -d '{
    "qrPayload": "SMARTLOG:STU-2026-001:1773032563:+HRvXV3FLt6N...",
    "scannedAt": "2026-03-09T09:30:00Z",
    "scanType": "ENTRY"
  }'
```

---

### Test QR Codes

**Location:** `/Users/markmarmeto/Projects/SmartLogScannerApp/test-qr-codes/`

**Files:**
- `test_qr_codes.html` - Visual QR codes (open in browser)
- `generate_test_qr.py` - Python script to regenerate
- `README.md` - Testing instructions

**Test Students:**
1. Juan Dela Cruz (STU-2026-001) - Grade 11 A
2. Maria Santos (STU-2026-002) - Grade 11 B
3. Pedro Reyes (STU-2026-003) - Grade 12 A
4. Ana Lopez (STU-2026-004) - Grade 12 B
5. Carlos Garcia (STU-2026-005) - Grade 10 C

**HMAC Secret:** `test-hmac-qr-code-2026`

**Open Test Page:**
```bash
open /tmp/test_qr_codes_new.html
```

---

### Quick Start Testing (Ready Now!)

**Everything is configured and ready to test:**

1. **Mock Server** - Already running on port 7001 (PID 31723)
2. **App Configuration** - Updated with `test-hmac-qr-code-2026`
3. **Test QR Codes** - Available at `/tmp/test_qr_codes_new.html`

**To test immediately:**

```bash
# 1. Navigate to project directory
cd /Users/markmarmeto/Projects/SmartLogScannerApp

# 2. Run the scanner app
dotnet run --project SmartLog.Scanner/SmartLog.Scanner.csproj --framework net8.0-maccatalyst

# 3. Test QR codes are already open in browser
# Point camera at screen and scan Juan Dela Cruz QR code

# Expected result:
# ✅ GREEN feedback: "✓ Juan Dela Cruz - Grade 11 A"
```

**For detailed testing scenarios, see:**
- 📄 `docs/testing-guide.md` - Complete testing guide with all scenarios
- 📄 `docs/server-error-fix.md` - Server fix documentation
- 📄 `test-qr-codes/setup_test_config_updated.sh` - Configuration script

---

## Development Workflow

### Building Scanner App

```bash
cd /Users/markmarmeto/Projects/SmartLogScannerApp

# Clean
dotnet clean

# Build
dotnet build SmartLog.Scanner/SmartLog.Scanner.csproj

# Run (macOS)
dotnet run --project SmartLog.Scanner/SmartLog.Scanner.csproj \
  --framework net8.0-maccatalyst

# Run tests
dotnet test SmartLog.Scanner.Tests/SmartLog.Scanner.Tests.csproj
```

### Building Web App

```bash
cd /Users/markmarmeto/Projects/SmartLogWebApp

# Build
dotnet build

# Run
dotnet run --project SmartLogWebApp/SmartLogWebApp.csproj

# Navigate to: http://localhost:7001
```

---

## Common Scenarios

### Scenario 1: New Installation

1. **Web App (IT Admin):**
   - Set HMAC secret: `test-hmac-qr-code-2026`
   - Register device, get API key
   - Add students, generate QR codes

2. **Scanner App (IT Admin):**
   - Launch app (first time)
   - SetupPage appears automatically
   - Enter server URL, API key, HMAC secret
   - Test connection → Save

3. **Scanner App (Guard):**
   - App starts to MainPage
   - Ready to scan

---

### Scenario 2: Daily Operation

1. Guard launches app
2. App loads in ENTRY mode (default)
3. Student shows QR
4. Camera scans → HMAC validates → Server submits
5. Green feedback with student name
6. Next student...

---

### Scenario 3: Changing Server

1. Guard clicks ⚙️ Settings
2. Update Server URL
3. Test Connection
4. Save
5. Return to scanning

---

### Scenario 4: Troubleshooting

**Scan shows "Invalid QR code":**
- Check: HMAC secret matches web app
- Check: QR code not damaged/tampered

**Scan shows "Network error":**
- Check: Server URL correct
- Check: Server running
- Check: Network connectivity

**Scan shows "Invalid API key":**
- Check: API key entered correctly
- Check: Device registered in web app

---

## Personas

### 1. Guard Gary (Primary User)

**Profile:**
- Security guard at school gate
- Age: 45-60
- Tech proficiency: Novice
- Education: High school

**Needs:**
- Dead-simple interface
- Instant visual feedback (colors)
- Audio confirmation (can look away)
- Zero decision-making
- Fast processing (hundreds of students in 30 min)

**Pain Points:**
- Cannot troubleshoot technical issues
- Gets confused by complex UIs
- Needs clear error messages
- Frustrated by delays

**Scanner App Design for Gary:**
- ✅ Large, clear visual feedback
- ✅ Auto-scan (no button press)
- ✅ Audio confirmation
- ✅ Color-coded statuses (green=good, red=bad)
- ✅ No technical jargon
- ✅ Auto-clear (no manual dismiss)

---

### 2. IT Admin Ian (Setup User)

**Profile:**
- School IT administrator
- Age: 30-45
- Tech proficiency: Advanced
- Education: College degree

**Needs:**
- One-time device configuration
- Bulk scanner deployment
- Clear error diagnostics
- Remote troubleshooting capability
- Secure credential management

**Pain Points:**
- Traveling to each gate for setup
- Unclear error messages
- Lost/forgotten API keys
- Certificate issues

**Scanner App Design for Ian:**
- ✅ Setup wizard with validation
- ✅ Test connection before saving
- ✅ Export/import configuration
- ✅ Clear error messages with solutions
- ✅ Self-signed TLS support

---

## Glossary

| Term | Definition |
|------|------------|
| **HMAC** | Hash-based Message Authentication Code - cryptographic signature |
| **QR Code** | Quick Response code - 2D barcode |
| **Keyboard Wedge** | USB device that sends keystrokes (appears as keyboard) |
| **MAUI** | Multi-platform App UI - .NET cross-platform framework |
| **Debounce** | Delay/filter to prevent rapid duplicate events |
| **Deduplication** | Logic to detect and prevent duplicate scans |
| **Offline Queue** | Local database storing scans when network unavailable |
| **SQLite** | Lightweight embedded database |
| **EF Core** | Entity Framework Core - ORM for .NET |
| **Polly** | Resilience library for .NET (retry, circuit breaker) |
| **Serilog** | Logging library for .NET |
| **ZXing** | "Zebra Crossing" - barcode/QR scanning library |
| **SecureStorage** | Platform-specific encrypted storage (Keychain/DPAPI) |
| **TLS** | Transport Layer Security - encryption for HTTPS |
| **API Key** | Secret token for authenticating API requests |

---

## Quick Reference

### Configuration Values (Current POC)

```
Server URL:     http://localhost:7001
API Key:        test-api-key-12345
HMAC Secret:    test-hmac-qr-code-2026
Device ID:      TEST-SCANNER-001
Scanner Mode:   Camera
Scan Type:      ENTRY
```

### File Paths

```
Scanner App:    /Users/markmarmeto/Projects/SmartLogScannerApp/
Web App:        /Users/markmarmeto/Projects/SmartLogWebApp/
Mock Server:    /tmp/mock-smartlog-server-updated.py
Test QR Codes:  /tmp/test_qr_codes_new.html
Config:         ~/Library/Containers/com.smartlog.scanner/Data/Library/Preferences/config.json
```

### Key Commands

```bash
# Start mock server
python3 /tmp/mock-smartlog-server-updated.py &

# Run scanner app
cd /Users/markmarmeto/Projects/SmartLogScannerApp
dotnet run --project SmartLog.Scanner/SmartLog.Scanner.csproj --framework net8.0-maccatalyst

# Open test QR codes
open /tmp/test_qr_codes_new.html

# View logs
tail -f ~/Library/Containers/com.smartlog.scanner/Data/Library/Application\ Support/logs/smartlog-scanner.log
```

### Test Scan (curl)

```bash
curl -X POST http://localhost:7001/api/v1/scans \
  -H "Content-Type: application/json" \
  -H "X-API-Key: test-api-key-12345" \
  -d '{
    "qrPayload": "SMARTLOG:STU-2026-001:1773032563:+HRvXV3FLt6NsJlXuBH6DRRc4CI3ZfhJXHfg+3+uRFY=",
    "scannedAt": "2026-03-09T09:30:00Z",
    "scanType": "ENTRY"
  }'
```

---

## Document Maintenance

**Purpose:** Primary context document for Claude Code running in separate terminals outside project directories

**Update Frequency:** As needed when architecture or features change

**Owner:** Development Team

**Last Reviewed:** 2026-02-16

**Next Review:** When starting new epic or major feature

**Recent Changes:**
- 2026-02-16: Updated with server fix, teal theme, EP0002 completion, testing infrastructure
- 2026-03-09: Initial comprehensive document creation

---

## For Claude Code Users

This document provides complete context for working on SmartLog project from any terminal location.

**When starting a new Claude Code session, ask for:**
- "Read `/Users/markmarmeto/Desktop/smartlog-project-context.md`" - Get full project context
- "What's the current status?" - Check Recent Updates section
- "Show me the quick start" - Jump to Quick Start Testing section

**Key sections for different tasks:**
- 🎯 **New feature** → System Architecture, Current Status
- 🐛 **Bug fix** → Process Flow, API Contract, Testing Setup
- 📝 **Documentation** → File Structure, Configuration
- 🧪 **Testing** → Quick Start Testing, Testing Setup
- 🔧 **Configuration** → Configuration, Quick Reference

---

**End of Document**
