# Epic Registry

**Last Updated:** 2026-05-06
**PRD Reference:** [Product Requirements Document](../prd.md)

## Summary

| Status | Count |
|--------|-------|
| Draft | 2 |
| Ready | 0 |
| Approved | 0 |
| In Progress | 0 |
| Done | 5 |
| **Total** | **7** |

## Epics

| ID | Title | Status | Owner | Stories | Target |
|----|-------|--------|-------|---------|--------|
| [EP0001](EP0001-device-setup-and-configuration.md) | Device Setup and Configuration | Done | AI Assistant | 5 | 1.0.0 |
| [EP0002](EP0002-qr-code-scanning-and-validation.md) | QR Code Scanning and Validation | Done | AI Assistant | 3 | 1.0.0 |
| [EP0003](EP0003-scan-processing-and-feedback.md) | Scan Processing and Feedback | Done | AI Assistant | 5 | 1.0.0 |
| [EP0004](EP0004-offline-resilience-and-sync.md) | Offline Resilience and Sync | Done | AI Assistant | 4 | 1.0.0 |
| EP0011 (cross-project, see WebApp) | Multi-Camera Scanning | In Progress | AI Assistant | 11 | 2.0.0 / 2.1.0 |
| [EP0012](EP0012-concurrent-multi-modal-scanning.md) | Concurrent Multi-Modal Scanning | Draft | AI Assistant | 3 | 2.2.0 |
| [EP0018](EP0018-scanner-slim-down.md) | Scanner Slim-down | Draft | AI Assistant | 4 | 2.3.0 |

## Feature-to-Epic Mapping

| Feature | Epic | Rationale |
|---------|------|-----------|
| F01 Device Setup Wizard | EP0001 | IT Admin setup journey |
| F12 Secure Config Storage | EP0001 | Credential infrastructure |
| F13 Self-Signed TLS Support | EP0001 | Network infrastructure |
| F15 Global Exception Handling | EP0001 | App resilience infrastructure |
| F02 QR Scanning (Camera) | EP0002 | Guard Gary input pipeline |
| F03 QR Scanning (USB) | EP0002 | Guard Gary input pipeline |
| F04 Local QR Validation | EP0002 | Input validation gateway |
| F05 Scan Submission | EP0003 | Core scan processing |
| F06 Student Feedback Display | EP0003 | Guard Gary output UX |
| F10 Audio Feedback | EP0003 | Guard Gary output UX |
| F11 Scan Type Toggle | EP0003 | Scan workflow control |
| F14 Scan Statistics | EP0003 | Scan workflow feedback |
| F07 Offline Queue | EP0004 | Offline data persistence |
| F08 Background Sync | EP0004 | Offline data recovery |
| F09 Health Check Monitoring | EP0004 | Connectivity monitoring |

## Dependency Graph

```
EP0001 (Setup & Config)
  ├── EP0002 (Scanning & Validation)  [needs F12]
  │     └── EP0003 (Processing & Feedback)  [needs validated QR]
  └── EP0004 (Offline Resilience)  [needs F13]
        └── EP0003 (Processing & Feedback)  [needs offline queue]
```

## Notes

- Epics are numbered globally (EP0001, EP0002, etc.)
- Stories are tracked in [Story Registry](../stories/_index.md)
- All 15 PRD features mapped — no orphan features
- EP0011 (Multi-Camera Scanning) is tracked as a cross-project V2 epic in the WebApp registry; scanner-side stories US0066-US0071 are listed in this project's story registry for traceability.

## Changelog

| Date | Change |
|------|--------|
| 2026-02-13 | Initial epic registry created (EP0001-EP0004, Draft) |
| 2026-02-16 | All 4 V1 epics completed (see PROJECT-COMPLETION-REPORT.md) |
| 2026-04-22 | Status reconciliation — registry updated from Draft to Done; added EP0011 reference row |
| 2026-04-24 | EP0011 re-opened for V2.1 scanner-side additions (US0088-US0092). Scanner story count: 23 Done + 5 Draft = 28 total |
| 2026-04-28 | EP0012 (Concurrent Multi-Modal Scanning) added in Draft — adds `Both` mode to run cameras + USB scanner simultaneously, USB indicator slot card with 60s health heuristic. 3 stories (US0121-US0123), 13 points, target V2.2.0 |
| 2026-05-06 | EP0018 (Scanner Slim-down) added in Draft — bounded cleanup pass from 12-feature reevaluation review. 4 stories (US0128-US0131), 6 points, target V2.3.0. Drops AboutPage, lowers engine camera cap 8→4, inlines AdaptiveDecodeThrottle, skips heartbeat POST when offline. No user-visible behavior change at the gate. |
