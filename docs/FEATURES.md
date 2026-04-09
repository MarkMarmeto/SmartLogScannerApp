# SmartLog Scanner App — Features

SmartLog Scanner is a .NET MAUI desktop application for Windows (and macOS development) that runs at school entry/exit gates. It reads student QR codes and submits attendance scans to the SmartLog Web App server in real time.

---

## Setup & Configuration

- **First-launch Setup Wizard** — guided configuration on first run
- Enter server URL, API key, and HMAC secret through a validated form
- **Connection test** validates server reachability before saving
- Settings persisted for subsequent launches (no wizard again)
- Re-accessible at any time via the settings screen
- **Secure storage** — API key and HMAC secret stored via DPAPI (Windows) or Keychain (macOS), never in plain-text config files

---

## QR Code Scanning

### Camera Scanning
- Live camera feed with real-time QR detection (ZXing.Net.Maui)
- Cross-platform: AVFoundation on macOS, WinUI camera API on Windows

### USB Barcode Scanner (Keyboard Wedge)
- Supports any USB keyboard-wedge barcode scanner
- 100ms input timeout window to distinguish scanner input from keyboard
- Works without any special drivers — plug and play

### Local HMAC Pre-Validation
- QR payload validated locally before network submission
- HMAC-SHA256 constant-time comparison (prevents timing attacks)
- Invalid QR codes (wrong format or bad signature) rejected before hitting the server

---

## Scan Processing & Feedback

### ENTRY / EXIT Mode
- Toggle between ENTRY and EXIT scan types
- Persists across app restarts
- Clearly displayed in the UI at all times

### Color-Coded Result Display

| Color | Meaning |
|---|---|
| Green | Scan accepted — student name, grade, section shown |
| Amber | Duplicate scan — already scanned within dedup window |
| Red | Scan rejected — invalid QR, inactive student, etc. |
| Blue/Teal | Server-side informational response (e.g., not a school day) |

Result screen auto-clears after 3 seconds, ready for the next scan.

### Audio Feedback
Four distinct audio cues mapped to result types:
- Success beep (accepted)
- Neutral tone (duplicate)
- Error buzz (rejected)
- Critical alert (device unauthorized)

### Scan Statistics Footer
Live counters displayed at the bottom of the main screen:
- Total scans this session
- Accepted / Duplicate / Rejected counts

---

## Server Health Monitoring

- Background health check polls `GET /api/v1/health` every 15 seconds
- Visual connection status indicator (online/offline) always visible
- Scanner stops accepting new submissions when server is unreachable

---

## Scan Deduplication

Three-tier time windows to prevent duplicate entries:

| Tier | Window | Action |
|---|---|---|
| Suppress | 3 seconds | Silently ignored (accidental double-scan) |
| Warn | 60 seconds | Amber screen with duplicate warning |
| Server | 300 seconds | Forwarded to server for server-side dedup check |

---

## Offline Resilience (Implemented, Currently Disabled)

The offline queue is fully built but disabled by default ("always-online mode"):
- SQLite-backed queue stores scans that fail to submit
- Background sync service flushes the queue when connectivity is restored
- Polly retry policies with exponential backoff
- Circuit breaker pattern (5 failures → 30-second open state)
- Offline Queue page shows pending items and sync status

To enable: contact the development team.

---

## Scan History

- **Scan Logs page** shows history of all scans in the current session
- Timestamps, student IDs, and result statuses
- Useful for guard review and incident verification

---

## Performance

| Metric | Target | Achieved |
|---|---|---|
| Scan-to-feedback latency | < 500ms | ~200–300ms |
| Camera frame processing | 30 FPS | ~30 FPS |
| USB input timeout | 100ms | 100ms |
| Health check interval | 15s | 15s |
| Auto-clear timer | 3s | 3s |
