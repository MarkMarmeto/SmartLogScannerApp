# Product Requirements Document

**Project:** SmartLog Scanner
**Version:** 1.0.0
**Last Updated:** 2026-02-13
**Status:** Draft

---

## 1. Project Overview

### Product Name
SmartLog Scanner

### Purpose
Offline-capable cross-platform desktop application (.NET MAUI) installed on Windows PCs and Macs at school gates for QR-based student attendance tracking. Security guards scan student QR codes on entry/exit. The app validates scans locally via HMAC-SHA256, displays student information with color-coded feedback, and submits data to the SmartLog server — queuing locally via SQLite when the network is unavailable.

### Tech Stack

| Category | Technology |
|----------|-----------|
| Language | C# 12 |
| Runtime | .NET 8.0 (LTS) |
| UI Framework | .NET MAUI (Multi-platform App UI) |
| Target Platforms | macOS (MacCatalyst), Windows (WinUI 3) |
| MVVM Framework | CommunityToolkit.Mvvm |
| MAUI Extras | CommunityToolkit.Maui (converters, behaviors) |
| Local Database | SQLite via EF Core (Microsoft.EntityFrameworkCore.Sqlite) |
| HTTP Client | HttpClient with Polly (retry / circuit breaker) |
| QR Scanning | Camera: ZXing.Net.Maui / USB: keyboard wedge via raw key input |
| QR Decoding | ZXing.Net.Maui (MAUI-native binding) or ZXing.Net |
| Logging | Serilog (file + console sinks) |
| DI Container | Microsoft.Extensions.DependencyInjection (built into MAUI) |
| Configuration | Microsoft.Extensions.Configuration (appsettings.json as MauiAsset) |
| Secure Storage | MAUI SecureStorage (Keychain on macOS, DPAPI on Windows) |
| Audio | Plugin.Maui.Audio |
| Packaging | MSIX (Windows), .app bundle (macOS) |

### Architecture Pattern
MVVM (Model-View-ViewModel) with layered services. Shell navigation for page routing. Dependency injection via built-in MAUI DI container. Service interfaces for all business logic enabling testability.

### System Context
SmartLog Scanner is a **separate project** from the SmartLog Admin Web App (ASP.NET Core Razor Pages). It communicates with the web app via REST API over a school LAN. Device registration and API key provisioning occur in the web admin panel.

---

## 2. Problem Statement

### Problem Being Solved
Schools need an efficient, reliable way to track student attendance at entry and exit gates. Current manual processes are slow, error-prone, and break down during peak arrival/departure times when hundreds of students pass through in minutes. The system must continue working even when the network is temporarily unavailable.

### Target Users
**Primary:** "Guard Gary" — a novice-level security guard who needs instant visual feedback. He handles hundreds of students during peak times and cannot afford delays or confusion. The UI must be dead-simple: point, scan, see result.

**Secondary:** IT Administrator — configures and deploys the scanner application on gate PCs/Macs, registers devices in the admin panel, and troubleshoots connectivity issues.

### Context
- Deployed on school LAN (potentially with self-signed TLS certificates)
- Must work on both Windows and macOS gate machines
- Guards have minimal technical skills — interface must be foolproof
- Peak-time pressure: hundreds of scans in short windows
- Outdoor/bright conditions possible — high-contrast UI needed
- Network reliability varies — offline capability is critical

---

## 3. Feature Inventory

| Feature | Description | Status | Priority | Location |
|---------|-------------|--------|----------|----------|
| F01 Device Setup Wizard | One-time first-launch configuration | Not Started | Must-have | Views/SetupPage |
| F02 QR Scanning (Camera) | Camera-based QR code scanning | Not Started | Must-have | Services/QrScannerService |
| F03 QR Scanning (USB) | USB barcode scanner keyboard wedge input | Not Started | Must-have | Services/QrScannerService |
| F04 Local QR Validation | HMAC-SHA256 signature verification before server submission | Not Started | Must-have | Infrastructure/Security/HmacHelper |
| F05 Scan Submission | Submit validated scans to server REST API | Not Started | Must-have | Services/ScanApiService |
| F06 Student Feedback Display | Color-coded result display with student info | Not Started | Must-have | Views/MainPage |
| F07 Offline Queue | SQLite-based local queue when server unreachable | Not Started | Must-have | Services/OfflineQueueService |
| F08 Background Sync | Automatic sync of queued scans when connectivity restored | Not Started | Must-have | Services/OfflineQueueService |
| F09 Health Check Monitoring | Continuous server connectivity monitoring | Not Started | Must-have | Services/HealthCheckService |
| F10 Audio Feedback | Sound effects for scan results (success, duplicate, error, queued) | Not Started | Should-have | Services/SoundService |
| F11 Scan Type Toggle | ENTRY/EXIT mode toggle with persistence | Not Started | Must-have | ViewModels/MainViewModel |
| F12 Secure Config Storage | Encrypted storage of API key and HMAC secret | Not Started | Must-have | Services/SecureConfigService |
| F13 Self-Signed TLS Support | Accept self-signed certificates for LAN deployment | Not Started | Must-have | MauiProgram (HttpClient) |
| F14 Scan Statistics | Today's scan count and queue pending count | Not Started | Should-have | ViewModels/MainViewModel |
| F15 Global Exception Handling | Crash recovery with Serilog logging | Not Started | Must-have | MauiProgram / App.xaml.cs |

### Feature Details

#### F01: Device Setup Wizard

**User Story:** As an IT administrator, I want to configure the scanner device on first launch so that it connects to the correct SmartLog server with valid credentials.

**Acceptance Criteria:**
- [ ] Setup page is shown automatically on first launch when no configuration exists
- [ ] User can enter server URL (e.g., https://192.168.1.100:8443)
- [ ] User can paste API key (provided from admin panel device registration)
- [ ] User can enter shared HMAC secret key for local QR validation
- [ ] User can select scan mode: "Camera" or "USB Scanner (Keyboard Wedge)"
- [ ] User can select default scan type: ENTRY or EXIT
- [ ] "Test Connection" button verifies server connectivity and API key validity via GET /api/v1/health/details
- [ ] On success: API key and HMAC secret stored in MAUI SecureStorage; non-sensitive settings stored in Preferences; navigation proceeds to MainPage
- [ ] On failure: clear error message displayed; user can retry
- [ ] Setup.Completed preference flag guards navigation (false → SetupPage, true → MainPage)

**Dependencies:** F12 (Secure Config Storage), F09 (Health Check)
**Priority:** Must-have
**Confidence:** [HIGH]

#### F02: QR Scanning (Camera)

**User Story:** As Guard Gary, I want to scan student QR codes using the PC camera so that I can record attendance without manual data entry.

**Acceptance Criteria:**
- [ ] Camera preview displayed in the main scan area when camera mode is active
- [ ] Continuous frame capture with ZXing.Net.Maui QR decoding
- [ ] Debounce: ignore duplicate reads of the same QR payload within 2 seconds
- [ ] Decoded payload is passed to local validation (F04) before server submission
- [ ] Camera permissions requested on macOS (NSCameraUsageDescription in Info.plist)
- [ ] Webcam capability declared on Windows (Package.appxmanifest)

**Dependencies:** F04 (Local QR Validation)
**Priority:** Must-have
**Confidence:** [HIGH]

#### F03: QR Scanning (USB)

**User Story:** As Guard Gary, I want to scan student QR codes using a USB barcode scanner so that I can record attendance with dedicated scanning hardware.

**Acceptance Criteria:**
- [ ] USB scanner operates in keyboard wedge mode (simulates rapid keystrokes)
- [ ] App listens for rapid keyboard input matching QR pattern (SMARTLOG:...)
- [ ] Key-input buffer with ~100ms inter-keystroke timeout to distinguish scanner input from manual typing
- [ ] When complete QR payload received, auto-process through validation pipeline
- [ ] "Ready to Scan" message displayed when waiting for input
- [ ] Works on both Windows and macOS

**Dependencies:** F04 (Local QR Validation)
**Priority:** Must-have
**Confidence:** [HIGH]

#### F04: Local QR Validation (HMAC)

**User Story:** As a system, I want to verify QR code authenticity locally before contacting the server so that invalid/forged QR codes are rejected immediately without network traffic.

**Acceptance Criteria:**
- [ ] QR payload parsed: split by ":" expecting exactly 4 parts
- [ ] Prefix verified as "SMARTLOG"
- [ ] HMAC-SHA256 computed over "{studentId}:{timestamp}" using shared secret from SecureStorage
- [ ] Signature compared using CryptographicOperations.FixedTimeEquals() (constant-time)
- [ ] Valid QR → proceed to server submission (F05) or offline queue (F07)
- [ ] Invalid QR → immediate rejection (red feedback), NOT submitted to server
- [ ] QR format: SMARTLOG:{studentId}:{unixTimestamp}:{hmacBase64}

**Dependencies:** F12 (Secure Config Storage)
**Priority:** Must-have
**Confidence:** [HIGH]

#### F05: Scan Submission

**User Story:** As a system, I want to submit validated scans to the SmartLog server so that attendance is recorded centrally.

**Acceptance Criteria:**
- [ ] POST /api/v1/scans with X-API-Key header, JSON body: { qrPayload, scannedAt, scanType }
- [ ] Success (200, status=ACCEPTED): display green result with student name, grade, section, scan type, time
- [ ] Duplicate (200, status=DUPLICATE): display amber result with "Already scanned. Please proceed."
- [ ] Rejected (400, status=REJECTED): display red result with error message (InvalidQrCode, StudentInactive, QrCodeInvalidated)
- [ ] Auth error (401): display error, prompt to verify API key in settings
- [ ] Rate limited (429): respect Retry-After header; do not exceed 60 scans/minute
- [ ] Network error / timeout: seamlessly queue scan offline (F07)
- [ ] HttpClient configured with Polly retry policy and circuit breaker
- [ ] Server timeout: 10 seconds (configurable)

**Dependencies:** F04 (Local QR Validation), F07 (Offline Queue), F13 (Self-Signed TLS)
**Priority:** Must-have
**Confidence:** [HIGH]

#### F06: Student Feedback Display

**User Story:** As Guard Gary, I want to see clear, color-coded scan results with student information so that I instantly know if the scan was successful without reading detailed text.

**Acceptance Criteria:**
- [ ] Result area shows: student name (large text), grade/section, student ID, scan type, time
- [ ] Color coding: GREEN (#4CAF50) = ACCEPTED, AMBER (#FF9800) = DUPLICATE, RED (#F44336) = REJECTED, BLUE (#2196F3) = QUEUED (offline), GRAY = IDLE
- [ ] Status badge displayed prominently (ACCEPTED, DUPLICATE, REJECTED, QUEUED)
- [ ] Auto-clear: result display clears after 3 seconds, returning to "Ready to Scan"
- [ ] Offline mode: show "Scan queued (offline)" with blue color (student info unknown)
- [ ] All colors defined as application-level resources in AppStyles.xaml

**Dependencies:** F05 (Scan Submission)
**Priority:** Must-have
**Confidence:** [HIGH]

#### F07: Offline Queue

**User Story:** As Guard Gary, I want scans to be saved locally when the network is down so that no attendance data is lost during outages.

**Acceptance Criteria:**
- [ ] When server unreachable, locally-validated scans stored in SQLite queue
- [ ] QueuedScan entity: Id (PK), QrPayload, ScannedAt, ScanType, CreatedAt, SyncStatus (PENDING/SYNCED/FAILED), SyncAttempts (default 0), LastSyncError (nullable), ServerScanId (nullable)
- [ ] SQLite database stored at FileSystem.AppDataDirectory (cross-platform)
- [ ] EF Core context (ScannerDbContext) with migrations
- [ ] Guard sees blue "Scan queued (offline)" feedback
- [ ] Queue count shown in footer stats bar

**Dependencies:** F04 (Local QR Validation)
**Priority:** Must-have
**Confidence:** [HIGH]

#### F08: Background Sync

**User Story:** As a system, I want to automatically sync queued scans when the server becomes available so that attendance data is submitted without manual intervention.

**Acceptance Criteria:**
- [ ] Background service polls /api/v1/health every 15 seconds (configurable)
- [ ] When healthy, submits PENDING queued scans in FIFO order
- [ ] Batch size: up to 50 scans per sync cycle (configurable)
- [ ] On success: SyncStatus = "SYNCED", store ServerScanId
- [ ] On failure: increment SyncAttempts, store error, retry with exponential backoff
- [ ] Max 10 retry attempts (configurable) before marking as "FAILED"
- [ ] On app restart: automatically resume sync of any PENDING scans
- [ ] UI updates from background sync use MainThread.InvokeOnMainThreadAsync()

**Dependencies:** F07 (Offline Queue), F09 (Health Check)
**Priority:** Must-have
**Confidence:** [HIGH]

#### F09: Health Check Monitoring

**User Story:** As Guard Gary, I want to see whether the scanner is online or offline so that I know if scans are being submitted live or queued.

**Acceptance Criteria:**
- [ ] Status indicator in header bar: green dot + "Online" or red dot + "Offline"
- [ ] Polls GET /api/v1/health (no auth required) at configurable interval (15 seconds default)
- [ ] Connectivity state drives online/offline scan submission behavior
- [ ] Seamless transition: if server goes down mid-operation, switch to queue mode with no user action
- [ ] Authenticated detailed health check (GET /api/v1/health/details) used during setup test

**Dependencies:** F13 (Self-Signed TLS)
**Priority:** Must-have
**Confidence:** [HIGH]

#### F10: Audio Feedback

**User Story:** As Guard Gary, I want to hear distinct sounds for each scan result so that I get confirmation without looking at the screen.

**Acceptance Criteria:**
- [ ] Success (ACCEPTED): short pleasant beep (success.wav)
- [ ] Duplicate: double short beep (duplicate.wav)
- [ ] Rejected: long error tone (error.wav)
- [ ] Queued (offline): soft chime (queued.wav)
- [ ] Audio enabled/disabled via Preferences ("Scanner.SoundEnabled")
- [ ] Implemented via Plugin.Maui.Audio (cross-platform)
- [ ] Sound files stored in Resources/Raw/

**Dependencies:** None
**Priority:** Should-have
**Confidence:** [HIGH]

#### F11: Scan Type Toggle

**User Story:** As Guard Gary, I want to toggle between ENTRY and EXIT scan modes so that I record the correct direction of student movement.

**Acceptance Criteria:**
- [ ] Toggle control on main scan page: [ENTRY Mode] <-> [EXIT Mode]
- [ ] Current mode persisted to Preferences ("Scanner.DefaultScanType")
- [ ] App starts in last-used mode on launch
- [ ] Scan type sent with each scan submission

**Dependencies:** None
**Priority:** Must-have
**Confidence:** [HIGH]

#### F12: Secure Config Storage

**User Story:** As a system, I want API keys and HMAC secrets stored encrypted so that credentials are not exposed in plain text.

**Acceptance Criteria:**
- [ ] API key stored in MAUI SecureStorage (Keychain on macOS, DPAPI on Windows)
- [ ] HMAC secret key stored in MAUI SecureStorage
- [ ] SecureConfigService provides read/write abstraction via ISecureConfigService interface
- [ ] Non-sensitive preferences (server URL, scan mode, etc.) stored in MAUI Preferences
- [ ] Never store secrets in appsettings.json or plain text files

**Dependencies:** None
**Priority:** Must-have
**Confidence:** [HIGH]

#### F13: Self-Signed TLS Support

**User Story:** As a system, I want to accept self-signed TLS certificates so that the app works on school LANs with self-signed server certificates.

**Acceptance Criteria:**
- [ ] HttpClientHandler configured with ServerCertificateCustomValidationCallback
- [ ] Self-signed cert acceptance controlled by "AcceptSelfSignedCerts" config (default: true)
- [ ] Warning logged when self-signed cert acceptance is active
- [ ] Works on both macOS and Windows
- [ ] Named HttpClient registered via IHttpClientFactory in MauiProgram.cs

**Dependencies:** None
**Priority:** Must-have
**Confidence:** [HIGH]

#### F14: Scan Statistics

**User Story:** As Guard Gary, I want to see today's total scan count and pending queue size so that I have a sense of progress and system status.

**Acceptance Criteria:**
- [ ] Footer bar shows: "Queue: N pending | Today: N scans"
- [ ] Queue count updated in real-time as scans are queued/synced
- [ ] Today's count incremented for each successful online scan and each queued scan

**Dependencies:** F07 (Offline Queue)
**Priority:** Should-have
**Confidence:** [HIGH]

#### F15: Global Exception Handling

**User Story:** As a system, I want unhandled exceptions captured and logged so that the app recovers gracefully and issues are diagnosable.

**Acceptance Criteria:**
- [ ] AppDomain.CurrentDomain.UnhandledException handled with Serilog logging
- [ ] TaskScheduler.UnobservedTaskException handled with Serilog logging
- [ ] Log files written to FileSystem.AppDataDirectory/logs/ via Serilog file sink
- [ ] All scans (online and offline) logged for audit/troubleshooting
- [ ] App recovers gracefully from crashes; on restart, resumes sync of pending queued scans

**Dependencies:** None
**Priority:** Must-have
**Confidence:** [HIGH]

---

## 4. Functional Requirements

### Core Behaviors
1. **ALWAYS** validate QR locally first (HMAC check). Never send invalid QR data to the server.
2. **NEVER** store HMAC secret key or API key in plain text. Use MAUI SecureStorage exclusively.
3. Handle self-signed TLS certificates on both macOS and Windows.
4. USB Scanner mode: listen for rapid keyboard input matching QR pattern (SMARTLOG:...) with ~100ms inter-keystroke timeout buffer.
5. Camera mode: continuous frame capture with ZXing.Net.Maui decoding. Debounce same-payload reads within 2 seconds.
6. Rate limit: do not submit more than 60 scans/minute to the server. Respect 429 responses with Retry-After header.
7. All scans logged locally via Serilog for audit/troubleshooting.
8. Auto-start in last-used scan mode (ENTRY/EXIT) via Preferences persistence.
9. Graceful degradation: if server goes down mid-operation, seamlessly switch to offline queue mode.
10. Crash recovery: on restart, resume sync of any pending queued scans automatically.
11. Use FileSystem.AppDataDirectory for SQLite database and log file paths (cross-platform safe).
12. Use MainThread.InvokeOnMainThreadAsync() for all UI updates from background services.

### Input/Output Specifications

**QR Code Input Format:**
```
SMARTLOG:{studentId}:{unixTimestamp}:{hmacBase64}
Example: SMARTLOG:STU-2026-001:1706918400:K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=
```

**HMAC Computation:**
- Algorithm: HMAC-SHA256
- Data to sign: "{studentId}:{timestamp}"
- Key: shared secret from SecureStorage
- Output: Base64-encoded signature
- Comparison: CryptographicOperations.FixedTimeEquals()

**Scan Submission Request:**
```json
POST /api/v1/scans
Headers: X-API-Key: {device-api-key}
{
  "qrPayload": "SMARTLOG:STU-2026-001:1706918400:abc123hmac",
  "scannedAt": "2026-02-04T08:15:00Z",
  "scanType": "ENTRY"
}
```

**Success Response (200):**
```json
{
  "scanId": "guid",
  "studentId": "STU-2026-001",
  "studentName": "Maria Santos",
  "grade": "5",
  "section": "A",
  "scanType": "ENTRY",
  "scannedAt": "2026-02-04T08:15:00Z",
  "status": "ACCEPTED"
}
```

**Duplicate Response (200):**
```json
{
  "scanId": "original-guid",
  "studentId": "STU-2026-001",
  "studentName": "Maria Santos",
  "grade": "5",
  "section": "A",
  "scanType": "ENTRY",
  "scannedAt": "2026-02-04T08:15:00Z",
  "status": "DUPLICATE",
  "originalScanId": "original-guid",
  "message": "Already scanned. Please proceed."
}
```

**Error Responses:**
- 400: `{ "error": "InvalidQrCode", "message": "...", "status": "REJECTED" }`
- 400: `{ "error": "StudentInactive", "message": "...", "status": "REJECTED" }`
- 400: `{ "error": "QrCodeInvalidated", "message": "...", "status": "REJECTED" }`
- 401: `{ "error": "InvalidApiKey", "message": "Invalid or missing API key" }`
- 429: Too Many Requests (Retry-After header)

### Business Logic Rules
- QR codes failing local HMAC validation are rejected immediately — never sent to server
- Duplicate detection is server-side; client displays server's DUPLICATE response
- Offline scans are queued with PENDING status; student info is unknown until synced
- FAILED queue items (10+ retry attempts) require manual review
- Scan type (ENTRY/EXIT) is determined by the current toggle state, not the QR code content

---

## 5. Non-Functional Requirements

### Performance
- Scan-to-result feedback: < 500ms for online scans (excluding network latency)
- Camera QR decode: continuous at ≥ 15 fps
- USB scanner input processing: < 50ms after complete payload received
- Auto-clear timeout: 3 seconds (configurable)
- Background sync must not block UI thread

### Security
- API key and HMAC secret: encrypted via MAUI SecureStorage (Keychain / DPAPI)
- HMAC comparison: constant-time via CryptographicOperations.FixedTimeEquals()
- Self-signed TLS certificates: accepted with explicit configuration and logged warning
- No secrets in plain text configuration files, logs, or source code
- API key transmitted only over HTTPS (or HTTP for local dev)

### Scalability
- Handle 60+ scans/minute during peak times
- SQLite offline queue: support 10,000+ pending scans without degradation
- Background sync: process batches of 50 scans per cycle

### Availability
- Must function fully offline (local validation + queue)
- Automatic recovery from network outages with zero user intervention
- Automatic recovery from app crashes — resume pending sync on restart
- No data loss: every validated scan either submitted or queued

---

## 6. AI/ML Specifications

> Not applicable. This application does not use AI/ML components.

---

## 7. Data Architecture

### Data Models

**QueuedScan (SQLite Entity)**

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | INTEGER | PK AUTOINCREMENT | Unique queue entry ID |
| QrPayload | TEXT | NOT NULL | Full QR code payload |
| ScannedAt | TEXT | NOT NULL | ISO 8601 timestamp of scan |
| ScanType | TEXT | NOT NULL | "ENTRY" or "EXIT" |
| CreatedAt | TEXT | NOT NULL | When queued |
| SyncStatus | TEXT | NOT NULL, DEFAULT "PENDING" | PENDING, SYNCED, or FAILED |
| SyncAttempts | INTEGER | NOT NULL, DEFAULT 0 | Number of sync attempts |
| LastSyncError | TEXT | NULLABLE | Last error message |
| ServerScanId | TEXT | NULLABLE | Server-assigned scan ID after sync |

**ScanResult (API Response Model)**

| Property | Type | Description |
|----------|------|-------------|
| ScanId | string | Server-assigned GUID |
| StudentId | string | e.g., STU-2026-001 |
| StudentName | string | Full name |
| Grade | string | Grade level |
| Section | string | Section identifier |
| ScanType | string | ENTRY or EXIT |
| ScannedAt | DateTime | Timestamp |
| Status | string | ACCEPTED, DUPLICATE, REJECTED |
| OriginalScanId | string? | For duplicates |
| Message | string? | For duplicates/errors |

**DeviceConfig (Runtime Configuration)**

| Property | Storage | Description |
|----------|---------|-------------|
| ServerBaseUrl | Preferences | Server URL |
| ApiKey | SecureStorage | Encrypted API key |
| HmacSecretKey | SecureStorage | Encrypted HMAC secret |
| ScanMode | Preferences | "Camera" or "USB" |
| DefaultScanType | Preferences | "ENTRY" or "EXIT" |
| SoundEnabled | Preferences | Audio feedback toggle |
| SetupCompleted | Preferences | First-launch guard |

### Relationships and Constraints
- QueuedScan is a standalone local entity with no foreign keys
- ScanResult is a transient API response model (not persisted)
- DeviceConfig is split across SecureStorage (secrets) and Preferences (settings)

### Storage Mechanisms
- **SQLite** (via EF Core): offline scan queue, stored at `FileSystem.AppDataDirectory/scanner_queue.db`
- **MAUI SecureStorage**: API key, HMAC secret (Keychain on macOS, DPAPI on Windows)
- **MAUI Preferences**: non-sensitive runtime configuration
- **Serilog file sink**: audit logs at `FileSystem.AppDataDirectory/logs/`

---

## 8. Integration Map

### External Services

| Service | Protocol | Endpoint | Auth | Purpose |
|---------|----------|----------|------|---------|
| SmartLog Admin Web App | REST/HTTPS | Configurable base URL | API Key (X-API-Key header) | Scan submission, health checks |

### API Endpoints Consumed

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| POST | /api/v1/scans | X-API-Key | Submit scan data |
| GET | /api/v1/health | None | Basic health check (connectivity) |
| GET | /api/v1/health/details | X-API-Key | Detailed health (setup validation) |

### Authentication Methods
- API Key: generated during device registration in SmartLog Admin Web App
- Sent as `X-API-Key` HTTP header on authenticated requests
- Stored encrypted in MAUI SecureStorage

### Third-Party Dependencies

| Package | Purpose |
|---------|---------|
| CommunityToolkit.Mvvm | MVVM source generators, ObservableObject, RelayCommand |
| CommunityToolkit.Maui | Converters, behaviors, MAUI extensions |
| Microsoft.EntityFrameworkCore.Sqlite | SQLite database via EF Core |
| ZXing.Net.Maui | QR code scanning and decoding |
| Plugin.Maui.Audio | Cross-platform audio playback |
| Serilog + Serilog.Sinks.File + Serilog.Sinks.Console | Structured logging |
| Polly / Microsoft.Extensions.Http.Polly | HTTP retry and circuit breaker policies |

---

## 9. Configuration Reference

### Runtime Preferences (MAUI Preferences)

| Key | Description | Required | Default |
|-----|-------------|----------|---------|
| Server.BaseUrl | SmartLog server URL | Yes | — |
| Scanner.Mode | "Camera" or "USB" | Yes | "USB" |
| Scanner.DefaultScanType | "ENTRY" or "EXIT" | Yes | "ENTRY" |
| Scanner.SoundEnabled | Audio feedback toggle | No | true |
| Setup.Completed | First-launch guard | No | false |

### Secure Storage Keys (MAUI SecureStorage)

| Key | Description | Required |
|-----|-------------|----------|
| Server.ApiKey | Device API key (encrypted) | Yes |
| Security.HmacSecretKey | Shared HMAC secret (encrypted) | Yes |

### Bundled Configuration (appsettings.json — MauiAsset)

| Path | Description | Default |
|------|-------------|---------|
| Server.TimeoutSeconds | HTTP request timeout | 10 |
| Server.AcceptSelfSignedCerts | Allow self-signed TLS | true |
| Scanner.AutoClearSeconds | Result auto-clear delay | 3 |
| OfflineQueue.HealthCheckIntervalSeconds | Health poll interval | 15 |
| OfflineQueue.MaxRetryAttempts | Max sync retries before FAILED | 10 |
| OfflineQueue.SyncBatchSize | Scans per sync cycle | 50 |
| Logging.MinimumLevel | Serilog minimum level | "Information" |

### Feature Flags
None currently defined.

---

## 10. Quality Assessment

### Tested Functionality
Not yet implemented — greenfield project.

### Untested Areas
All features — to be addressed via TDD approach during implementation.

### Technical Debt
None — greenfield project.

---

## 11. Open Questions

- **Q:** Which specific ZXing.Net.Maui package should be used — `ZXing.Net.Maui` or `ZXing.Net.Maui.Controls`?
  **Context:** Both exist; need to verify MAUI desktop (non-mobile) compatibility and camera support on macOS/Windows.
  **Options:** ZXing.Net.Maui.Controls (more MAUI-native), ZXing.Net with manual camera integration.

- **Q:** What is the HMAC secret key format and length provided by the admin panel?
  **Context:** Affects validation and SecureStorage sizing. Need to know if it's a raw byte array, hex string, or base64 string.

- **Q:** Should the scanner support QR code expiry validation (checking if the unix timestamp is too old)?
  **Context:** The timestamp is part of the QR payload but current spec only validates HMAC signature, not time-based expiry. Server may handle expiry separately.

- **Q:** What happens when SecureStorage is unavailable (e.g., Keychain locked on macOS)?
  **Context:** Need a fallback or error strategy if the OS secure storage is inaccessible.

- **Q:** Is there a maximum offline queue duration before data should be considered stale?
  **Context:** If a device is offline for days, should queued scans still be submitted when connectivity returns?

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-02-13 | 1.0.0 | Initial PRD created — 15 features, .NET MAUI cross-platform (Windows + macOS) |

---

> **Confidence Markers:** [HIGH] clear from specification | [MEDIUM] inferred from patterns | [LOW] speculative
>
> **Status Values:** Complete | Partial | Stubbed | Broken | Not Started
