# EP0003: Scan Processing and Feedback

> **Status:** Done
> **Owner:** AI Assistant
> **Completed:** 2026-02-13
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Target Release:** 1.0.0

## Summary

Deliver the core scan processing workflow and user-facing feedback loop — from submitting validated scans to the server, through color-coded visual results, audio confirmation, ENTRY/EXIT mode toggling, and scan statistics. This epic represents the primary interface Guard Gary interacts with hundreds of times per shift.

## Inherited Constraints

> See PRD and TRD for full constraint details. Key constraints for this epic:

| Source | Type | Constraint | Impact |
|--------|------|------------|--------|
| PRD | Performance | Scan-to-result feedback < 500ms (online, excluding network latency) | API call + UI update must be fast; use async/await, no blocking |
| PRD | UX | Auto-clear result after 3 seconds; high-contrast colors | Timer-based UI reset; color resources in AppStyles.xaml |
| TRD | Architecture | MVVM — MainViewModel drives all scan state | ObservableProperty for result binding; RelayCommand for toggle |
| TRD | Tech Stack | Polly retry + circuit breaker on HttpClient | F05 must handle transient failures gracefully via Polly policies |

---

## Business Context

### Problem Statement
After a QR code is scanned and validated locally, Guard Gary needs to see the result instantly — green for accepted, amber for duplicate, red for rejected, blue for queued offline. He also needs audio confirmation so he can glance away while students pass. The scan type (ENTRY/EXIT) must be easily toggleable at shift changeover.

**PRD Reference:** [Feature Inventory](../prd.md#3-feature-inventory)

### Value Proposition
This epic delivers the moment-to-moment experience Guard Gary relies on: scan → see color → hear sound → next student. The entire interaction should feel instantaneous and require zero decision-making. Statistics give him a sense of progress throughout the shift.

### Success Metrics

| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Scan-to-feedback latency (online) | N/A | < 500ms | Stopwatch from QR decode to UI update |
| Guard comprehension accuracy | N/A | 100% (color = status) | User testing with Guard Gary persona |
| Audio feedback coverage | N/A | 4 distinct sounds for 4 states | Functional test of all scan result paths |
| Auto-clear reliability | N/A | 100% clear after 3 seconds | Timer-based automated test |

---

## Scope

### In Scope
- Scan submission to POST /api/v1/scans with X-API-Key header
- Response parsing for ACCEPTED, DUPLICATE, REJECTED statuses
- 401 (Invalid API Key) error handling with user-facing message
- 429 (Rate Limited) handling with Retry-After header respect
- Network error detection → seamless handoff to offline queue (F07)
- Color-coded result display: GREEN/AMBER/RED/BLUE/GRAY
- Student info display: name (large), grade, section, ID, scan type, time
- Status badge (ACCEPTED, DUPLICATE, REJECTED, QUEUED)
- Auto-clear after 3 seconds returning to "Ready to Scan"
- Audio feedback: success.wav, duplicate.wav, error.wav, queued.wav
- Audio enable/disable via Preferences
- Plugin.Maui.Audio for cross-platform playback
- ENTRY/EXIT toggle control on MainPage
- Toggle persisted to Preferences; app starts in last-used mode
- Footer statistics: "Queue: N pending | Today: N scans"
- Real-time queue count and today's count updates

### Out of Scope
- Scan history list or log viewer UI
- Detailed student profile display
- Scan reversal or undo functionality
- Custom audio file upload
- Configurable color themes

### Affected Personas
- **Guard Gary:** Primary user — sees every scan result, hears audio feedback, toggles ENTRY/EXIT, glances at statistics
- **IT Admin Ian:** Indirect — may check statistics or toggle state when troubleshooting

---

## Acceptance Criteria (Epic Level)

- [ ] POST /api/v1/scans submits with X-API-Key header and JSON body (qrPayload, scannedAt, scanType)
- [ ] ACCEPTED response → green result with student name, grade, section, scan type, time
- [ ] DUPLICATE response → amber result with "Already scanned. Please proceed."
- [ ] REJECTED response → red result with error message
- [ ] 401 response → error display prompting API key verification
- [ ] 429 response → respect Retry-After header; max 60 scans/minute
- [ ] Network error → seamless queue to offline with blue "Scan queued (offline)" feedback
- [ ] HttpClient uses Polly retry policy and circuit breaker
- [ ] Result auto-clears after 3 seconds
- [ ] Distinct audio plays for each result type (ACCEPTED, DUPLICATE, REJECTED, QUEUED)
- [ ] Audio can be enabled/disabled via Preferences setting
- [ ] ENTRY/EXIT toggle visible on main scan page
- [ ] Toggle state persisted to Preferences and restored on app launch
- [ ] Scan type included in submission payload
- [ ] Footer shows "Queue: N pending | Today: N scans" with real-time updates
- [ ] All colors defined as application-level resources in AppStyles.xaml

---

## Dependencies

### Blocked By

| Dependency | Type | Status | Owner |
|------------|------|--------|-------|
| EP0001: Device Setup and Configuration | Epic | Draft | Unassigned |
| EP0002: QR Code Scanning & Validation | Epic | Draft | Unassigned |
| EP0004: Offline Resilience & Sync | Epic | Draft | Unassigned |
| F04: Local QR Validation | Feature | Not Started | — |
| F07: Offline Queue | Feature | Not Started | — |
| F12: Secure Config Storage | Feature | Not Started | — |
| F13: Self-Signed TLS Support | Feature | Not Started | — |

### Blocking

| Item | Type | Impact |
|------|------|--------|
| None — terminal epic in dependency chain | — | — |

---

## Risks & Assumptions

### Assumptions
- Plugin.Maui.Audio plays .wav files reliably on both macOS and Windows
- Polly policies are configured in EP0001 (IHttpClientFactory setup)
- Color resources in AppStyles.xaml are accessible from MainPage bindings
- Server response times are within acceptable range for < 500ms total feedback time

### Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Audio playback latency adds to scan-to-feedback time | Medium | Medium | Pre-load audio files on app start; play async without awaiting |
| Auto-clear timer conflicts with rapid scanning | Low | Medium | Cancel pending clear when new scan arrives |
| 429 rate limiting triggered during peak hours | Low | High | Client-side rate tracking; queue excess scans locally |
| Server response format changes break parsing | Low | High | Defensive deserialization with fallback error messages |

---

## Technical Considerations

### Architecture Impact
- MainViewModel becomes the central scan orchestrator: receives validated QR → submits → updates UI
- IScanApiService interface for server communication with Polly-wrapped HttpClient
- ISoundService interface wrapping Plugin.Maui.Audio for testability
- ScanResultModel: transient view-state model binding to result display
- Color resources and styles defined in AppStyles.xaml (application-level ResourceDictionary)
- Timer service for auto-clear functionality (CancellationTokenSource-based)

### Integration Points
- POST /api/v1/scans — scan submission (authenticated)
- EP0002 pipeline — receives validated QR payloads
- EP0004 offline queue — handoff when server unreachable
- Plugin.Maui.Audio — cross-platform audio playback
- MAUI Preferences — scan type toggle persistence, audio enable/disable

---

## Sizing

**Story Points:** 22
**Estimated Story Count:** 5

**Complexity Factors:**
- Multiple API response types to handle (ACCEPTED, DUPLICATE, REJECTED, 401, 429, network error)
- Color-coded UI with auto-clear timing logic
- Audio feedback integration across platforms
- Real-time statistics counter updates from multiple sources (online scans, offline queue changes)
- Polly policy interaction with offline queue handoff

---

## Story Breakdown

- [ ] [US0009: Implement Scan Type Toggle (ENTRY/EXIT)](../stories/US0009-scan-type-toggle.md)
- [ ] [US0010: Implement Scan Submission to Server API](../stories/US0010-scan-submission-to-server-api.md)
- [ ] [US0011: Implement Color-Coded Student Feedback Display](../stories/US0011-color-coded-student-feedback-display.md)
- [ ] [US0012: Implement Audio Feedback for Scan Results](../stories/US0012-audio-feedback-for-scan-results.md)
- [ ] [US0013: Implement Scan Statistics Footer](../stories/US0013-scan-statistics-footer.md)

---

## Test Plan

> Test spec to be generated via `/sdlc-studio test-spec --epic EP0003`

---

## Open Questions

- [ ] Should the auto-clear timer be configurable by Guard Gary, or fixed at 3 seconds? - Owner: Product
- [ ] What happens if audio hardware is unavailable (no speakers connected)? Silent failure or visual warning? - Owner: UX

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial epic created from PRD features F05, F06, F10, F11, F14 |
