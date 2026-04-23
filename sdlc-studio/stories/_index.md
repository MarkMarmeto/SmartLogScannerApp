# Story Registry

**Last Updated:** 2026-04-22
**Personas Reference:** [User Personas](../personas.md)

## Summary

| Status | Count |
|--------|-------|
| Draft | 0 |
| Ready | 0 |
| Planned | 0 |
| In Progress | 0 |
| Review | 0 |
| Done | 23 |
| **Total** | **23** |

## Stories by Epic

### [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0001](US0001-secure-configuration-storage-service.md) | Implement Secure Configuration Storage Service | Done | 5 | AI Assistant |
| [US0002](US0002-self-signed-tls-and-http-client-infrastructure.md) | Configure Self-Signed TLS and HTTP Client Infrastructure | Done | 5 | AI Assistant |
| [US0003](US0003-global-exception-handling-and-logging.md) | Implement Global Exception Handling and Logging | Done | 3 | AI Assistant |
| [US0004](US0004-device-setup-wizard-page.md) | Build Device Setup Wizard Page | Done | 5 | AI Assistant |
| [US0005](US0005-setup-connection-validation.md) | Implement Setup Connection Validation | Done | 5 | AI Assistant |

### [EP0002: QR Code Scanning and Validation](../epics/EP0002-qr-code-scanning-and-validation.md)

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0006](US0006-local-hmac-qr-validation.md) | Implement Local HMAC-SHA256 QR Validation | Done | 5 | AI Assistant |
| [US0007](US0007-camera-based-qr-scanning.md) | Implement Camera-Based QR Scanning | Done | 8 | AI Assistant |
| [US0008](US0008-usb-barcode-scanner-input.md) | Implement USB Barcode Scanner Keyboard Wedge Input | Done | 8 | AI Assistant |

### [EP0003: Scan Processing and Feedback](../epics/EP0003-scan-processing-and-feedback.md)

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0009](US0009-scan-type-toggle.md) | Implement Scan Type Toggle (ENTRY/EXIT) | Done | 3 | AI Assistant |
| [US0010](US0010-scan-submission-to-server-api.md) | Implement Scan Submission to Server API | Done | 8 | AI Assistant |
| [US0011](US0011-color-coded-student-feedback-display.md) | Implement Color-Coded Student Feedback Display | Done | 5 | AI Assistant |
| [US0012](US0012-audio-feedback-for-scan-results.md) | Implement Audio Feedback for Scan Results | Done | 3 | AI Assistant |
| [US0013](US0013-scan-statistics-footer.md) | Implement Scan Statistics Footer | Done | 3 | AI Assistant |

### [EP0004: Offline Resilience and Sync](../epics/EP0004-offline-resilience-and-sync.md)

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0014](US0014-sqlite-offline-scan-queue.md) | Implement SQLite Offline Scan Queue | Done | 5 | AI Assistant |
| [US0015](US0015-health-check-monitoring-service.md) | Implement Health Check Monitoring Service | Done | 5 | AI Assistant |
| [US0016](US0016-background-sync-service.md) | Implement Background Sync Service | Done | 8 | AI Assistant |
| [US0017](US0017-seamless-online-offline-transitions.md) | Implement Seamless Online/Offline State Transitions | Done | 5 | AI Assistant |

> **Note:** Offline mode is intentionally disabled per user decision ("always-online mode"). All offline code is implemented and tested; activation is a runtime configuration change.

### EP0011: Multi-Camera Scanning (cross-project — epic tracked in WebApp)

| ID | Title | Status | Points | Owner |
|----|-------|--------|--------|-------|
| [US0066](US0066-multi-camera-manager-core.md) | Multi-Camera Manager Core | Done | 8 | AI Assistant |
| [US0067](US0067-adaptive-decode-throttle.md) | Adaptive Decode Throttle | Done | 2 | AI Assistant |
| [US0068](US0068-main-page-camera-grid-ui.md) | Main Page Camera Grid UI | Done | 5 | AI Assistant |
| [US0069](US0069-per-camera-scan-type.md) | Per-Camera Scan Type | Done | 3 | AI Assistant |
| [US0070](US0070-error-isolation-and-recovery.md) | Error Isolation and Auto-Recovery | Done | 5 | AI Assistant |
| [US0071](US0071-multi-camera-setup-configuration.md) | Multi-Camera Setup Configuration | Done | 5 | AI Assistant |

## All Stories

| ID | Title | Epic | Status | Points | Persona |
|----|-------|------|--------|--------|---------|
| [US0001](US0001-secure-configuration-storage-service.md) | Secure Configuration Storage Service | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Done | 5 | IT Admin Ian |
| [US0002](US0002-self-signed-tls-and-http-client-infrastructure.md) | Self-Signed TLS and HTTP Client Infrastructure | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Done | 5 | System |
| [US0003](US0003-global-exception-handling-and-logging.md) | Global Exception Handling and Logging | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Done | 3 | System |
| [US0004](US0004-device-setup-wizard-page.md) | Device Setup Wizard Page | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Done | 5 | IT Admin Ian |
| [US0005](US0005-setup-connection-validation.md) | Setup Connection Validation | [EP0001](../epics/EP0001-device-setup-and-configuration.md) | Done | 5 | IT Admin Ian |
| [US0006](US0006-local-hmac-qr-validation.md) | Local HMAC-SHA256 QR Validation | [EP0002](../epics/EP0002-qr-code-scanning-and-validation.md) | Done | 5 | System |
| [US0007](US0007-camera-based-qr-scanning.md) | Camera-Based QR Scanning | [EP0002](../epics/EP0002-qr-code-scanning-and-validation.md) | Done | 8 | Guard Gary |
| [US0008](US0008-usb-barcode-scanner-input.md) | USB Barcode Scanner Keyboard Wedge Input | [EP0002](../epics/EP0002-qr-code-scanning-and-validation.md) | Done | 8 | Guard Gary |
| [US0009](US0009-scan-type-toggle.md) | Scan Type Toggle (ENTRY/EXIT) | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Done | 3 | Guard Gary |
| [US0010](US0010-scan-submission-to-server-api.md) | Scan Submission to Server API | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Done | 8 | System |
| [US0011](US0011-color-coded-student-feedback-display.md) | Color-Coded Student Feedback Display | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Done | 5 | Guard Gary |
| [US0012](US0012-audio-feedback-for-scan-results.md) | Audio Feedback for Scan Results | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Done | 3 | Guard Gary |
| [US0013](US0013-scan-statistics-footer.md) | Scan Statistics Footer | [EP0003](../epics/EP0003-scan-processing-and-feedback.md) | Done | 3 | Guard Gary |
| [US0014](US0014-sqlite-offline-scan-queue.md) | SQLite Offline Scan Queue | [EP0004](../epics/EP0004-offline-resilience-and-sync.md) | Done | 5 | System |
| [US0015](US0015-health-check-monitoring-service.md) | Health Check Monitoring Service | [EP0004](../epics/EP0004-offline-resilience-and-sync.md) | Done | 5 | Guard Gary |
| [US0016](US0016-background-sync-service.md) | Background Sync Service | [EP0004](../epics/EP0004-offline-resilience-and-sync.md) | Done | 8 | System |
| [US0017](US0017-seamless-online-offline-transitions.md) | Seamless Online/Offline State Transitions | [EP0004](../epics/EP0004-offline-resilience-and-sync.md) | Done | 5 | Guard Gary |
| [US0066](US0066-multi-camera-manager-core.md) | Multi-Camera Manager Core | EP0011 (WebApp) | Done | 8 | Guard Gary |
| [US0067](US0067-adaptive-decode-throttle.md) | Adaptive Decode Throttle | EP0011 (WebApp) | Done | 2 | Guard Gary |
| [US0068](US0068-main-page-camera-grid-ui.md) | Main Page Camera Grid UI | EP0011 (WebApp) | Done | 5 | Guard Gary |
| [US0069](US0069-per-camera-scan-type.md) | Per-Camera Scan Type | EP0011 (WebApp) | Done | 3 | Guard Gary |
| [US0070](US0070-error-isolation-and-recovery.md) | Error Isolation and Auto-Recovery | EP0011 (WebApp) | Done | 5 | Guard Gary |
| [US0071](US0071-multi-camera-setup-configuration.md) | Multi-Camera Setup Configuration | EP0011 (WebApp) | Done | 5 | Guard Gary |

## Notes

- Stories are numbered globally (US0001, US0002, etc.)
- Total story points: 117 (V1: 89 pts across 17 stories; EP0011 Multi-Camera: 28 pts across 6 stories)

## Changelog

| Date | Change |
|------|--------|
| 2026-02-13 | Initial story registry created (17 V1 stories, Draft) |
| 2026-02-16 | All 17 V1 stories completed (see PROJECT-COMPLETION-REPORT.md) |
| 2026-03-09 | 6 multi-camera stories (US0066-US0071) completed under cross-project EP0011 |
| 2026-04-22 | Status reconciliation — registry updated from stale Draft/Ready/Review markers to Done for all 23 stories |
