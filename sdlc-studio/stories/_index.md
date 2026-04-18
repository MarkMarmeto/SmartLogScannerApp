# Story Registry

**Last Updated:** 2026-02-13
**Personas Reference:** [User Personas](../personas.md)

## Summary

| Status | Count |
|--------|-------|
| Draft | 12 |
| Ready | 4 |
| Planned | 0 |
| In Progress | 0 |
| Review | 1 |
| Done | 0 |
| **Total** | **17** |

## Stories by Epic

### [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0001](US0001-secure-configuration-storage-service.md) | Implement Secure Configuration Storage Service | Review | 5 | Unassigned |
| [US0002](US0002-self-signed-tls-and-http-client-infrastructure.md) | Configure Self-Signed TLS and HTTP Client Infrastructure | Ready | 5 | Unassigned |
| [US0003](US0003-global-exception-handling-and-logging.md) | Implement Global Exception Handling and Logging | Ready | 3 | Unassigned |
| [US0004](US0004-device-setup-wizard-page.md) | Build Device Setup Wizard Page | Ready | 5 | Unassigned |
| [US0005](US0005-setup-connection-validation.md) | Implement Setup Connection Validation | Ready | 5 | Unassigned |

### [EP0002: QR Code Scanning and Validation](../epics/EP0002-qr-code-scanning-and-validation.md)

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0006](US0006-local-hmac-qr-validation.md) | Implement Local HMAC-SHA256 QR Validation | Draft | 5 | Unassigned |
| [US0007](US0007-camera-based-qr-scanning.md) | Implement Camera-Based QR Scanning | Draft | 8 | Unassigned |
| [US0008](US0008-usb-barcode-scanner-input.md) | Implement USB Barcode Scanner Keyboard Wedge Input | Draft | 8 | Unassigned |

### [EP0003: Scan Processing and Feedback](../epics/EP0003-scan-processing-and-feedback.md)

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0009](US0009-scan-type-toggle.md) | Implement Scan Type Toggle (ENTRY/EXIT) | Draft | 3 | Unassigned |
| [US0010](US0010-scan-submission-to-server-api.md) | Implement Scan Submission to Server API | Draft | 8 | Unassigned |
| [US0011](US0011-color-coded-student-feedback-display.md) | Implement Color-Coded Student Feedback Display | Draft | 5 | Unassigned |
| [US0012](US0012-audio-feedback-for-scan-results.md) | Implement Audio Feedback for Scan Results | Draft | 3 | Unassigned |
| [US0013](US0013-scan-statistics-footer.md) | Implement Scan Statistics Footer | Draft | 3 | Unassigned |

### [EP0004: Offline Resilience and Sync](../epics/EP0004-offline-resilience-and-sync.md)

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0014](US0014-sqlite-offline-scan-queue.md) | Implement SQLite Offline Scan Queue | Draft | 5 | Unassigned |
| [US0015](US0015-health-check-monitoring-service.md) | Implement Health Check Monitoring Service | Draft | 5 | Unassigned |
| [US0016](US0016-background-sync-service.md) | Implement Background Sync Service | Draft | 8 | Unassigned |
| [US0017](US0017-seamless-online-offline-transitions.md) | Implement Seamless Online/Offline State Transitions | Draft | 5 | Unassigned |

## All Stories

| ID | Title | Epic | Status | Points | Persona |
|----|-------|------|--------|--------|---------|
| [US0001](US0001-secure-configuration-storage-service.md) | Secure Configuration Storage Service | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Review | 5 | IT Admin Ian |
| [US0002](US0002-self-signed-tls-and-http-client-infrastructure.md) | Self-Signed TLS and HTTP Client Infrastructure | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Ready | 5 | System |
| [US0003](US0003-global-exception-handling-and-logging.md) | Global Exception Handling and Logging | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Ready | 3 | System |
| [US0004](US0004-device-setup-wizard-page.md) | Device Setup Wizard Page | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Ready | 5 | IT Admin Ian |
| [US0005](US0005-setup-connection-validation.md) | Setup Connection Validation | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Ready | 5 | IT Admin Ian |
| [US0006](US0006-local-hmac-qr-validation.md) | Local HMAC-SHA256 QR Validation | [EP0002](../epics/EP0002-qr-code-scanning-and-validation.md) | Draft | 5 | System |
| [US0007](US0007-camera-based-qr-scanning.md) | Camera-Based QR Scanning | [EP0002](../epics/EP0002-qr-code-scanning-and-validation.md) | Draft | 8 | Guard Gary |
| [US0008](US0008-usb-barcode-scanner-input.md) | USB Barcode Scanner Keyboard Wedge Input | [EP0002](../epics/EP0002-qr-code-scanning-and-validation.md) | Draft | 8 | Guard Gary |
| [US0009](US0009-scan-type-toggle.md) | Scan Type Toggle (ENTRY/EXIT) | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Draft | 3 | Guard Gary |
| [US0010](US0010-scan-submission-to-server-api.md) | Scan Submission to Server API | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Draft | 8 | System |
| [US0011](US0011-color-coded-student-feedback-display.md) | Color-Coded Student Feedback Display | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Draft | 5 | Guard Gary |
| [US0012](US0012-audio-feedback-for-scan-results.md) | Audio Feedback for Scan Results | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Draft | 3 | Guard Gary |
| [US0013](US0013-scan-statistics-footer.md) | Scan Statistics Footer | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Draft | 3 | Guard Gary |
| [US0014](US0014-sqlite-offline-scan-queue.md) | SQLite Offline Scan Queue | [EP0004](../epics/EP0004-offline-resilience-and-sync.md) | Draft | 5 | System |
| [US0015](US0015-health-check-monitoring-service.md) | Health Check Monitoring Service | [EP0004](../epics/EP0004-offline-resilience-and-sync.md) | Draft | 5 | Guard Gary |
| [US0016](US0016-background-sync-service.md) | Background Sync Service | [EP0004](../epics/EP0004-offline-resilience-and-sync.md) | Draft | 8 | System |
| [US0017](US0017-seamless-online-offline-transitions.md) | Seamless Online/Offline State Transitions | [EP0004](../epics/EP0004-offline-resilience-and-sync.md) | Draft | 5 | Guard Gary |

### EP0011: Multi-Camera Scanning

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0066](US0066-multi-camera-manager-core.md) | Multi-Camera Manager Core | Done | 8 | Unassigned |
| [US0067](US0067-adaptive-decode-throttle.md) | Adaptive Decode Throttle | Done | 2 | Unassigned |
| [US0068](US0068-main-page-camera-grid-ui.md) | Main Page Camera Grid UI | Done | 5 | Unassigned |
| [US0069](US0069-per-camera-scan-type.md) | Per-Camera Scan Type | Done | 3 | Unassigned |
| [US0070](US0070-error-isolation-and-recovery.md) | Error Isolation and Auto-Recovery | Done | 5 | Unassigned |
| [US0071](US0071-multi-camera-setup-configuration.md) | Multi-Camera Setup Configuration | Done | 5 | Unassigned |

## Notes

- Stories are numbered globally (US0001, US0002, etc.)
- Story points should be assigned during team refinement
- Total story points: 117
