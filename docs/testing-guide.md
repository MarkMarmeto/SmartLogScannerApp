# SmartLog Scanner - Testing Guide

**Status:** ✅ Ready to Test
**Date:** 2026-02-16
**Mock Server:** Running on port 7001 (PID 31723)
**App Configuration:** Updated with new HMAC secret

---

## What's Ready

### ✅ Mock Server (Running)
- **URL:** http://localhost:7001
- **HMAC Secret:** `test-hmac-qr-code-2026`
- **Student Database:** 5 test students loaded
- **Features:**
  - HMAC signature validation
  - Student name lookup
  - Duplicate detection (30-second window)
  - Proper error responses

### ✅ App Configuration
- **Server URL:** http://localhost:7001
- **API Key:** test-api-key-12345
- **HMAC Secret:** test-hmac-qr-code-2026
- **Scan Type:** ENTRY
- **Offline Mode:** Disabled

### ✅ Test QR Codes
- **Location:** `/tmp/test_qr_codes_new.html` (already opened in browser)
- **Students:**
  1. Juan Dela Cruz (STU-2026-001) - Grade 11 A
  2. Maria Santos (STU-2026-002) - Grade 11 B
  3. Pedro Reyes (STU-2026-003) - Grade 12 A
  4. Ana Lopez (STU-2026-004) - Grade 12 B
  5. Carlos Garcia (STU-2026-005) - Grade 10 C

---

## How to Test

### Step 1: Start the App
```bash
cd ~/Projects/SmartLogScannerApp
dotnet run --project SmartLog.Scanner/SmartLog.Scanner.csproj --framework net8.0-maccatalyst
```

### Step 2: Test Camera Scanning
1. Click the **Camera** mode (should be selected by default)
2. Point camera at Juan Dela Cruz QR code on screen
3. Watch for GREEN feedback with student name

### Step 3: Test USB Keyboard Input
1. Click to focus on the app window
2. Manually type the payload from the HTML page:
   ```
   SMARTLOG:STU-2026-001:1773032563:+HRvXV3FLt6NsJlXuBH6DRRc4CI3ZfhJXHfg+3+uRFY=
   ```
3. Press ENTER
4. Watch for GREEN feedback with student name

---

## Expected Test Results

### ✅ First Scan (Juan Dela Cruz)
**Expected Response:**
```
🟢 GREEN Feedback
Message: "✓ Juan Dela Cruz - Grade 11 A"
Time: Current timestamp
Status: ENTRY
Sound: Success beep
Duration: 5 seconds
```

**Server Log:**
```
📥 Scan Request:
   Student ID: STU-2026-001
   HMAC Expected: +HRvXV3FLt6NsJlXuBH6DRRc4CI3ZfhJXHfg+3+uRFY=
   HMAC Received: +HRvXV3FLt6NsJlXuBH6DRRc4CI3ZfhJXHfg+3+uRFY=
   ✅ HMAC verification SUCCESS
   ✅ ACCEPTED - Juan Dela Cruz (Grade 11 A)
```

### ⚠️ Duplicate Scan (Same Student < 30s)
**Expected Response:**
```
🟠 AMBER Feedback
Message: "⚠ Juan Dela Cruz already scanned. Please proceed."
Time: Current timestamp
Status: DUPLICATE
Sound: Duplicate warning
Duration: 3 seconds
```

**Server Log:**
```
⚠️ DUPLICATE - STU-2026-001 scanned 5 seconds ago
```

### ✅ Different Student (Maria Santos)
**Expected Response:**
```
🟢 GREEN Feedback
Message: "✓ Maria Santos - Grade 11 B"
Time: Current timestamp
Status: ENTRY
Sound: Success beep
Duration: 5 seconds
```

### ❌ Invalid QR Code
**Test:** Scan a random QR code or modify payload
**Expected Response:**
```
🔴 RED Feedback
Message: "Invalid QR code signature"
Time: Current timestamp
Duration: 5 seconds
```

---

## Verification Checklist

- [ ] App starts without errors
- [ ] Camera preview shows when Camera mode selected
- [ ] USB mode accepts keyboard input
- [ ] First scan shows GREEN with student name "Juan Dela Cruz"
- [ ] Duplicate scan (< 30s) shows AMBER warning
- [ ] Different student shows GREEN with correct name
- [ ] Invalid QR shows RED error
- [ ] Toggle ENTRY/EXIT works (both scan types independent)
- [ ] Settings button navigates to settings page (teal theme)
- [ ] Health check shows green status indicator

---

## Test Scenarios

### Scenario 1: Normal Entry Flow
1. Scan Juan → GREEN "✓ Juan Dela Cruz - Grade 11 A"
2. Scan Maria → GREEN "✓ Maria Santos - Grade 11 B"
3. Scan Pedro → GREEN "✓ Pedro Reyes - Grade 12 A"
4. **Result:** All three unique students accepted

### Scenario 2: Duplicate Detection
1. Scan Juan → GREEN (accepted)
2. Wait 5 seconds
3. Scan Juan again → AMBER "⚠ Juan Dela Cruz already scanned"
4. Wait 30 seconds
5. Scan Juan again → Server decides (could be GREEN or AMBER)
6. **Result:** Client-side dedup catches within 30s

### Scenario 3: ENTRY/EXIT Independence
1. Set scan type to ENTRY
2. Scan Juan → GREEN
3. Toggle to EXIT
4. Scan Juan → GREEN (different scan type, not a duplicate)
5. Toggle back to ENTRY
6. Scan Juan → AMBER (duplicate ENTRY detected)
7. **Result:** ENTRY and EXIT are independent dedup keys

### Scenario 4: Invalid Signatures
1. Manually type invalid payload: `SMARTLOG:INVALID:123:ABC123==`
2. Press ENTER
3. **Result:** RED "Invalid QR code signature"

---

## Troubleshooting

### Issue: "Unexpected error occurred"
**Cause:** Server not running or wrong configuration
**Fix:**
```bash
# Check server is running
ps aux | grep mock-smartlog-server-updated.py

# Restart server if needed
kill $(cat /tmp/server.pid)
python3 /tmp/mock-smartlog-server-updated.py &
```

### Issue: "Invalid QR code signature"
**Cause:** HMAC secret mismatch
**Fix:**
```bash
# Verify app config
cat ~/Library/Containers/com.smartlog.scanner/Data/Library/Preferences/config.json

# Should show: "HmacSecret": "test-hmac-qr-code-2026"
# If wrong, re-run setup script:
/Users/markmarmeto/Projects/SmartLogScannerApp/test-qr-codes/setup_test_config_updated.sh
```

### Issue: Camera not showing preview
**Cause:** macOS camera permissions
**Fix:**
1. System Settings → Privacy & Security → Camera
2. Enable for SmartLog Scanner

### Issue: USB input not working
**Cause:** App window not focused
**Fix:** Click on the app window before typing

---

## Server Management

### Check Server Status
```bash
ps aux | grep mock-smartlog-server
curl http://localhost:7001/api/v1/health
```

### View Server Logs
```bash
tail -f /tmp/server.log
```

### Stop Server
```bash
kill $(cat /tmp/server.pid)
```

### Restart Server
```bash
python3 /tmp/mock-smartlog-server-updated.py &
```

---

## Test Files

| File | Purpose |
|------|---------|
| `/tmp/test_qr_codes_new.html` | Visual QR codes (browser) |
| `/tmp/mock-smartlog-server-updated.py` | Mock API server |
| `/tmp/generate_new_qr.py` | QR code generator script |
| `/tmp/server.log` | Server request/response log |
| `/tmp/server.pid` | Server process ID |

---

## Success Criteria

**All tests pass when:**
1. ✅ Unique students show GREEN feedback with correct names
2. ✅ Duplicate scans (< 30s) show AMBER warnings with cached names
3. ✅ Invalid QR codes show RED errors
4. ✅ ENTRY/EXIT scan types work independently
5. ✅ Camera and USB modes both function correctly
6. ✅ Server logs show HMAC validation details
7. ✅ UI theme is teal/green throughout
8. ✅ Settings page accessible and themed correctly

---

**Ready to test!** Run the app and start scanning the QR codes. The mock server is already running and the app is configured with the correct HMAC secret.
