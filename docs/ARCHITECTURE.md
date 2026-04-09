# SmartLog — System Architecture

This document describes how SmartLogWebApp and SmartLogScannerApp fit together as a system: their roles, how they communicate, and how data flows through a typical school day.

For per-app internals (stack, solution structure, services), see each app's `docs/TECHNICAL.md`.

---

## System Overview

SmartLog is a two-application system deployed entirely on the school's local network. No internet connection is required for core operations.

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         School LAN (192.168.1.x)                         │
│                                                                           │
│  ┌───────────────────────┐      HTTP/REST       ┌──────────────────────┐ │
│  │  SmartLogScannerApp   │ ──────────────────►  │   SmartLogWebApp     │ │
│  │  (.NET 8 MAUI)        │  POST /api/v1/scans  │   (ASP.NET Core 8)   │ │
│  │                       │  GET  /api/v1/health │                      │ │
│  │  Gate scanner device  │ ◄──────────────────  │  Server / admin PC   │ │
│  │  (Windows laptop/PC)  │   scan result JSON   │  Port 5050           │ │
│  └───────────────────────┘                      │                      │ │
│                                                  │  ┌────────────────┐  │ │
│  ┌───────────────────────┐                      │  │  SQL Server    │  │ │
│  │  SmartLogScannerApp   │                      │  │  (Express)     │  │ │
│  │  (second gate, etc.)  │ ──────────────────►  │  └────────────────┘  │ │
│  └───────────────────────┘                      │                      │ │
│                                                  │  ┌────────────────┐  │ │
│  ┌───────────────────────┐                      │  │  GSM Modem     │  │ │
│  │  Admin Browser        │ ──── HTTP ─────────► │  │  (USB serial)  │  │ │
│  │  (any PC on LAN)      │   Razor Pages UI     │  └────────────────┘  │ │
│  └───────────────────────┘                      └──────────────────────┘ │
│                                                           │               │
│                                                           │ SMS           │
│                                                           ▼               │
│                                                    Parent mobile phones   │
└─────────────────────────────────────────────────────────────────────────┘
```

| Component | Role | Deployment |
|---|---|---|
| **SmartLogWebApp** | Central server — attendance records, student data, SMS, admin UI | Windows Service on a dedicated school PC |
| **SmartLogScannerApp** | Gate client — reads QR codes, submits scans, shows real-time feedback | Windows desktop app on each gate PC |
| **SQL Server Express** | Persistent data store for all records | Co-located with SmartLogWebApp |
| **GSM Modem** | SMS gateway for parent notifications (offline-capable) | USB-connected to the SmartLogWebApp server |
| **Semaphore API** | Cloud SMS fallback (requires internet) | External; used only when GSM modem fails |

---

## Roles & Responsibilities

### SmartLogWebApp (Server)

- **Source of truth** for all student, attendance, and configuration data
- Validates QR codes (HMAC-SHA256 verification)
- Enforces business rules (school calendar, duplicate detection, student status)
- Queues and sends SMS notifications to parents
- Provides the admin UI (Razor Pages) for managing students, devices, reports, and settings
- Exposes a REST API for scanner clients

### SmartLogScannerApp (Client)

- Reads QR codes via camera or USB barcode scanner
- Performs **local HMAC pre-validation** before sending to the server (rejects obviously invalid codes without a network round-trip)
- Submits scans to the server and displays the result to the gate guard
- Monitors server health every 15 seconds
- Multiple scanner instances can run simultaneously — each with its own API key, all connecting to the same server

---

## Communication Protocol

The Scanner App and Web App communicate exclusively over **HTTP/REST** on the school's LAN.

### Endpoints Used by the Scanner App

| Method | Endpoint | Auth | Purpose |
|---|---|---|---|
| `POST` | `/api/v1/scans` | `X-API-Key` header | Submit a QR scan, receive result |
| `GET` | `/api/v1/health` | None | Check server is reachable |

### Scanner → Server Request Flow

```
1.  Student presents QR code
2.  Scanner reads payload: SMARTLOG:{studentId}:{timestamp}:{hmac}
3.  Scanner validates HMAC locally (rejects bad format / tampered codes immediately)
4.  Scanner sends POST /api/v1/scans  ──►  Server
        X-API-Key: sk_live_xxx
        { qrPayload, scannedAt, scanType: "ENTRY" }
5.  Server authenticates device (SHA-256 hash lookup, constant-time compare)
6.  Server re-validates HMAC
7.  Server checks: student active? school day? duplicate within 5 min?
8.  Server saves Scan record to SQL Server
9.  Server queues SMS notification (SmsQueue table)
10. Server responds  ◄──  Scanner
        { studentName, grade, section, status: "ACCEPTED" }
11. Scanner shows color-coded result + plays audio cue
12. SmsWorkerService (background) sends SMS via GSM modem or Semaphore
```

### Response Status Values

| Status | Meaning | Scanner Display |
|---|---|---|
| `ACCEPTED` | Scan recorded | Green screen |
| `DUPLICATE` | Already scanned within 5 min | Amber screen |
| `REJECTED_INVALID_QR` | Bad format or HMAC mismatch | Red screen |
| `REJECTED_QR_INVALIDATED` | QR was regenerated; this code is revoked | Red screen |
| `REJECTED_STUDENT_INACTIVE` | Student account deactivated | Red screen |
| `REJECTED_NOT_SCHOOL_DAY` | Holiday, suspension, or weekend | Blue/teal screen |

---

## Authentication & Trust Model

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Trust Boundaries                              │
│                                                                       │
│  Scanner App                           Web App                       │
│  ┌──────────────────┐                 ┌──────────────────────────┐   │
│  │ API Key          │ ──X-API-Key──►  │ Hash(key) == Device.Hash │   │
│  │ (DPAPI-encrypted)│                 │ Constant-time compare    │   │
│  └──────────────────┘                 └──────────────────────────┘   │
│                                                                       │
│  ┌──────────────────┐                 ┌──────────────────────────┐   │
│  │ HMAC Secret      │  pre-validates  │ Re-validates HMAC        │   │
│  │ (DPAPI-encrypted)│ ──────────────► │ HMAC-SHA256(id:ts)       │   │
│  └──────────────────┘                 │ Constant-time compare    │   │
│                                       └──────────────────────────┘   │
│                                                                       │
│  Admin Browser                         Web App                       │
│  ┌──────────────────┐                 ┌──────────────────────────┐   │
│  │ Cookie session   │ ──── HTTP ────► │ ASP.NET Core Identity    │   │
│  │ (10-hr sliding)  │                 │ Role-based policies      │   │
│  └──────────────────┘                 └──────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

**API Key:** Each scanner device has a unique API key (`sk_live_xxx`). The Web App stores only the SHA-256 hash. The Scanner stores the plain key in platform secure storage (DPAPI on Windows, Keychain on macOS). Keys are never in config files or logs.

**HMAC Secret:** Shared between the Web App (used to sign QR codes when they are generated) and the Scanner App (used for local pre-validation). The scanner validates locally first, then the server re-validates independently. Both store it via platform secure storage.

**Admin sessions:** Standard cookie-based auth with role-based access control (SuperAdmin, Admin, Teacher, Security, Staff).

---

## QR Code Lifecycle

```
QR Code Generation (Web App)
    │
    ├─ Admin generates QR: SMARTLOG:{studentId}:{timestamp}:{HMAC}
    ├─ HMAC = SHA256(studentId:timestamp) using SMARTLOG_HMAC_SECRET_KEY
    ├─ Saved to QrCode table (IsValid = true)
    └─ Printed on student ID card

QR Code Use (Scanner App → Web App)
    │
    ├─ Scanner reads payload from ID card
    ├─ Scanner validates HMAC locally (rejects if bad)
    ├─ Scanner sends payload to server
    ├─ Server re-validates HMAC
    └─ Server checks QrCode.IsValid (admin can revoke)

QR Code Revocation (Web App)
    │
    ├─ Admin regenerates QR for a student
    ├─ Old QrCode.IsValid set to false
    └─ Any future scan of the old QR → REJECTED_QR_INVALIDATED
```

The timestamp embedded in the QR code does **not** expire — it is only used as input to the HMAC signature, not for time-based validity. Deduplication (5-minute window) is enforced server-side per scan submission.

---

## SMS Notification Flow

```
Scan accepted by Web App
    │
    ▼
ScanService.QueueAttendanceNotificationAsync()
    ├─ Is SMS globally enabled?          (AppSettings / SmsSettings)
    ├─ Does student have a phone number? (Student.ParentPhone)
    ├─ Is SMS enabled for this student?  (Student.SmsEnabled)
    ├─ What language?                    (Student.SmsLanguage → EN or FIL)
    ├─ Render template                   (SmsTemplate: ENTRY or EXIT)
    ├─ Duplicate check (5-min window)
    └─ INSERT into SmsQueue (Status=Pending)
              │
              ▼
    SmsWorkerService  (background, polls every 5s)
    ├─ Fetch next Pending message (ordered by Priority, then ScheduledAt)
    ├─ Try GSM_MODEM (default)
    │       ├─ Success → Status=Sent, log to SmsLog
    │       └─ Failure → try fallback if enabled
    ├─ Try SEMAPHORE (fallback, if configured)
    │       ├─ Success → Status=Sent, log to SmsLog
    │       └─ Failure → Status=Failed, schedule retry
    └─ Retry with exponential backoff: 2 min → 4 min → 8 min (max 3 retries)
```

**SMS Gateways:**

| Gateway | Transport | Internet Required | Cost |
|---|---|---|---|
| GSM Modem | USB serial AT commands | No | ~₱1/SMS (SIM load) |
| Semaphore | HTTPS REST API | Yes | Per-credit pricing |

---

## Offline Resilience (Scanner App)

The Scanner App is designed to handle temporary server unavailability:

```
Normal operation:
  QR scan → HMAC local pre-validation → POST /api/v1/scans → display result

When server is unreachable:
  QR scan → HMAC local pre-validation → ScanApiService fails
                                              │
                                              ▼
                                    OfflineQueueService
                                    INSERT into SQLite QueuedScans

When connectivity is restored:
  BackgroundSyncService (polls health endpoint every 15s)
      │
      ├─ Server reachable? → flush QueuedScans to server in batches
      └─ Success → delete from queue / mark synced
```

> **Note:** Offline mode is implemented but currently disabled by default ("always-online mode"). The Scanner App rejects scans when the server is unreachable unless offline mode is re-enabled.

---

## Deployment Topology

```
School Building
│
├── Server Room / Office
│   └── Server PC (Windows)
│       ├── SmartLogWebApp (Windows Service, port 5050)
│       ├── SQL Server Express
│       ├── USB GSM Modem ──────────────────────────────► Parent phones (SMS)
│       └── Static IP: 192.168.1.10
│
├── Main Gate
│   └── Gate PC (Windows)
│       ├── SmartLogScannerApp (desktop app)
│       ├── Camera or USB barcode scanner
│       └── Connected to school Wi-Fi or Ethernet
│
├── Back Gate
│   └── Gate PC (Windows)
│       ├── SmartLogScannerApp (desktop app)
│       └── Connected to school Wi-Fi or Ethernet
│
└── School Wi-Fi Router (192.168.1.1)
    └── All devices on same subnet (192.168.1.x)
```

**Key constraints:**
- The server must have a **static IP** — all scanner devices use it as their server URL
- All devices must be on the **same LAN subnet** — no internet or cross-subnet routing required
- Each gate PC registers as a separate device in the Web App (separate API key per device)
- The admin UI is accessible from **any browser on the LAN** — no dedicated admin machine required

---

## Data Storage Summary

| Data | Store | Location |
|---|---|---|
| Students, attendance, SMS, settings | SQL Server | SmartLogWebApp server |
| API key hash, device registrations | SQL Server | SmartLogWebApp server |
| QR code images (base64) | SQL Server | SmartLogWebApp server |
| Offline scan queue | SQLite | Gate PC (`AppData/smartlog-scanner.db`) |
| API key (plain) | DPAPI / Keychain | Gate PC (secure storage) |
| HMAC secret (plain) | DPAPI / Keychain | Gate PC (secure storage) |
| Server URL, scan mode | MAUI Preferences | Gate PC (registry) |
