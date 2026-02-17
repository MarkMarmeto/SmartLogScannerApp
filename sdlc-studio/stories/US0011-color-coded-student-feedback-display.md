# US0011: Implement Color-Coded Student Feedback Display

> **Status:** Draft
> **Epic:** [EP0003: Scan Processing and Feedback](../epics/EP0003-scan-processing-and-feedback.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As a** Guard Gary
**I want** to see a large, color-coded display showing the student's name, grade, section, and scan status after each QR scan
**So that** I can instantly determine whether the student is accepted, a duplicate, or rejected without reading detailed text, and the display automatically clears for the next student

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency. Processes hundreds of students during peak times. Needs instant, unambiguous visual feedback -- green means good, red means problem. Cannot afford to read fine print.
[Full persona details](../personas.md#guard-gary)

### Background
The scan result display is the single most important UI element in the entire application. Guard Gary glances at it hundreds of times per shift. The color must be unmistakable at arm's length: GREEN for accepted, AMBER for duplicate, RED for rejected, BLUE for offline-queued. The student's name must be displayed in large text so Gary can visually confirm the right student passed through. After 3 seconds, the display automatically returns to a neutral GRAY "Ready to Scan" state, preparing for the next student. This auto-clear ensures the previous result does not confuse Gary when the next student approaches.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | UX | High-contrast colors; large text readable at arm's length | Student name must be minimum 32pt; status badge minimum 24pt; colors must meet WCAG AA contrast ratio against backgrounds |
| PRD | UX | Auto-clear result after 3 seconds | Timer resets on each new scan; uses CancellationTokenSource pattern |
| TRD | Architecture | MVVM with CommunityToolkit.Mvvm | ScanResultViewModel or properties on MainViewModel; ObservableProperty bindings to XAML |
| TRD | Architecture | Colors defined as application-level resources in AppStyles.xaml | Color hex values referenced via StaticResource or DynamicResource in XAML |
| Epic | Performance | Scan-to-result feedback < 500ms | UI update must happen on MainThread immediately upon receiving ScanResult |

---

## Acceptance Criteria

### AC1: Result area displays student information for ACCEPTED scan
- **Given** a scan has been submitted and the server returns ScanResult with Status=Accepted, StudentName="Maria Santos", Grade="Grade 7", Section="Section A", StudentId="1001", ScanType="ENTRY", ScannedAt="2026-02-13T07:30:00Z"
- **When** the result is received by MainViewModel
- **Then** the result area on MainPage displays: student name "Maria Santos" in large bold text (minimum 32pt), "Grade 7 - Section A" below the name, "ID: 1001" below grade/section, "ENTRY" scan type indicator, "07:30 AM" formatted scan time, and a prominent status badge reading "ACCEPTED"

### AC2: GREEN color for ACCEPTED status
- **Given** a ScanResult with Status=Accepted is received
- **When** the result area is updated
- **Then** the result area background color is GREEN (#4CAF50), text color provides sufficient contrast (white #FFFFFF), and the status badge reads "ACCEPTED" on the green background

### AC3: AMBER color for DUPLICATE status
- **Given** a ScanResult with Status=Duplicate, Message="Already scanned. Please proceed." is received
- **When** the result area is updated
- **Then** the result area background is AMBER (#FF9800), student info is displayed as normal, the status badge reads "DUPLICATE", and the message "Already scanned. Please proceed." is displayed below the status badge

### AC4: RED color for REJECTED status
- **Given** a ScanResult with Status=Rejected, ErrorReason="StudentInactive", Message="Student account is inactive. Please contact the registrar." is received
- **When** the result area is updated
- **Then** the result area background is RED (#F44336), the status badge reads "REJECTED", the error message from the server is displayed prominently, and student info fields may be empty (no student data returned for rejected scans)

### AC5: BLUE color for QUEUED (offline) status
- **Given** a ScanResult with Status=Queued, Message="Scan queued (offline)" is received (network failure)
- **When** the result area is updated
- **Then** the result area background is BLUE (#2196F3), the status badge reads "QUEUED", the message "Scan queued (offline)" is displayed, and student info fields show "---" or are hidden (student data unavailable when offline)

### AC6: GRAY idle state with "Ready to Scan" message
- **Given** no scan has been processed yet, or the auto-clear timer has elapsed
- **When** the result area is in idle state
- **Then** the result area background is GRAY (#9E9E9E), a centered message reads "Ready to Scan" in large text, and no student info fields are displayed

### AC7: Auto-clear after 3 seconds
- **Given** a scan result is currently displayed (any status: ACCEPTED, DUPLICATE, REJECTED, or QUEUED)
- **When** 3 seconds elapse without a new scan
- **Then** the result area transitions back to the GRAY idle state showing "Ready to Scan"

### AC8: New scan cancels pending auto-clear
- **Given** an ACCEPTED result is displayed and the 3-second auto-clear timer is running with 1.5 seconds remaining
- **When** a new scan result arrives (e.g., DUPLICATE)
- **Then** the previous auto-clear timer is cancelled, the result area immediately updates to show the new DUPLICATE result with AMBER background, and a new 3-second auto-clear timer starts

### AC9: Colors defined as application-level resources
- **Given** the application resource dictionary (AppStyles.xaml or equivalent)
- **When** color values are referenced
- **Then** the following colors are defined as application-level Color resources: StatusAccepted=#4CAF50, StatusDuplicate=#FF9800, StatusRejected=#F44336, StatusQueued=#2196F3, StatusIdle=#9E9E9E, and are referenced in the result area via StaticResource

### AC10: UI updates dispatched to MainThread
- **Given** ScanResult is returned from IScanApiService on a background thread
- **When** the ViewModel updates the result display properties
- **Then** all UI-bound property changes are dispatched via MainThread.InvokeOnMainThreadAsync() to prevent cross-thread access exceptions

---

## Scope

### In Scope
- Result area layout on MainPage.xaml (student name, grade/section, student ID, scan type, time, status badge)
- MainViewModel properties: CurrentScanResult (bound to result area), ResultBackgroundColor, ResultStatusText, IsResultVisible, IsIdleVisible
- IValueConverter: ScanStatusToColorConverter (maps ScanStatus enum to Color resource)
- Auto-clear timer (3 seconds) using CancellationTokenSource
- Application-level color resources in AppStyles.xaml
- Data binding from MainViewModel to result area XAML elements
- Formatting helpers: time display (HH:mm format), grade/section concatenation
- Unit tests for ViewModel logic, timer behavior, and value converter

### Out of Scope
- Audio feedback (covered by US0012)
- Scan submission logic (covered by US0010)
- Scan type toggle (covered by US0009)
- Animation or transition effects between states
- Scan history list or log viewer
- Detailed student profile or photo display
- Configurable auto-clear duration (fixed at 3 seconds)

---

## Technical Notes

### Implementation Details
- **MainViewModel** scan result properties:
  ```csharp
  [ObservableProperty]
  private ScanResult? _currentScanResult;

  [ObservableProperty]
  private ScanDisplayState _displayState = ScanDisplayState.Idle;

  [ObservableProperty]
  private Color _resultBackgroundColor;

  [ObservableProperty]
  private string _statusBadgeText = "Ready to Scan";

  [ObservableProperty]
  private string _studentNameDisplay = "";

  [ObservableProperty]
  private string _gradeAndSectionDisplay = "";

  [ObservableProperty]
  private string _studentIdDisplay = "";

  [ObservableProperty]
  private string _scanTimeDisplay = "";

  [ObservableProperty]
  private string _resultMessageDisplay = "";

  private CancellationTokenSource? _autoClearCts;
  ```
- **Auto-clear timer pattern:**
  ```csharp
  private async Task StartAutoClearTimerAsync()
  {
      _autoClearCts?.Cancel();
      _autoClearCts = new CancellationTokenSource();
      var token = _autoClearCts.Token;

      try
      {
          await Task.Delay(TimeSpan.FromSeconds(3), token);
          await MainThread.InvokeOnMainThreadAsync(() => SetIdleState());
      }
      catch (OperationCanceledException)
      {
          // Timer cancelled by new scan - expected
      }
  }
  ```
- **ScanStatusToColorConverter:**
  ```csharp
  public class ScanStatusToColorConverter : IValueConverter
  {
      public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
      {
          if (value is ScanStatus status)
          {
              return status switch
              {
                  ScanStatus.Accepted => Application.Current.Resources["StatusAccepted"] as Color,
                  ScanStatus.Duplicate => Application.Current.Resources["StatusDuplicate"] as Color,
                  ScanStatus.Rejected => Application.Current.Resources["StatusRejected"] as Color,
                  ScanStatus.Queued => Application.Current.Resources["StatusQueued"] as Color,
                  _ => Application.Current.Resources["StatusIdle"] as Color,
              };
          }
          return Application.Current.Resources["StatusIdle"] as Color;
      }
  }
  ```
- **AppStyles.xaml** color resources:
  ```xml
  <Color x:Key="StatusAccepted">#4CAF50</Color>
  <Color x:Key="StatusDuplicate">#FF9800</Color>
  <Color x:Key="StatusRejected">#F44336</Color>
  <Color x:Key="StatusQueued">#2196F3</Color>
  <Color x:Key="StatusIdle">#9E9E9E</Color>
  ```

### API Contracts
Not directly applicable. This story consumes the ScanResult model returned by IScanApiService (US0010).

### Data Requirements

**ScanDisplayState Enum:**

| Value | Background Color | Status Badge Text | Visible Fields |
|-------|-----------------|-------------------|----------------|
| Idle | GRAY #9E9E9E | "Ready to Scan" | None (message only) |
| Accepted | GREEN #4CAF50 | "ACCEPTED" | Name, Grade/Section, ID, Type, Time |
| Duplicate | AMBER #FF9800 | "DUPLICATE" | Name, Grade/Section, ID, Type, Time, Message |
| Rejected | RED #F44336 | "REJECTED" | Error Message (student info may be absent) |
| Queued | BLUE #2196F3 | "QUEUED" | "Scan queued (offline)" message |

**Display Formatting:**

| Field | Source | Format | Example |
|-------|--------|--------|---------|
| Student Name | ScanResult.StudentName | As-is, Bold, 32pt+ | "Maria Santos" |
| Grade/Section | ScanResult.Grade + Section | "{Grade} - {Section}" | "Grade 7 - Section A" |
| Student ID | ScanResult.StudentId | "ID: {value}" | "ID: 1001" |
| Scan Type | MainViewModel.CurrentScanType | As-is from toggle | "ENTRY" |
| Scan Time | ScanResult.ScannedAt | "hh:mm tt" (local time) | "07:30 AM" |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| New scan arrives before 3-second auto-clear timer expires | Previous timer is cancelled via CancellationTokenSource.Cancel(); new result displayed immediately; new 3-second timer starts |
| Very long student name (e.g., "Maria Christina Dela Cruz-Santos de la Vega" - 50+ characters) | Name text truncates with ellipsis (...) if it exceeds the display width; minimum font size is maintained (32pt); no wrapping that would push other fields off-screen |
| Missing fields in response: null grade, null section | Display "---" for missing grade/section; do not show "null" literally; format as "---" or hide the grade/section line entirely |
| Rapid successive scans (3 scans within 1 second) | Each scan immediately replaces the previous display; only the last scan's 3-second timer runs; no visual flickering or stale data displayed |
| ScanResult update arrives on background thread | MainThread.InvokeOnMainThreadAsync() wraps all property updates; no InvalidOperationException from cross-thread UI access |
| App minimized or background during auto-clear timer | Timer continues to run; when app is restored, the display is in the correct state (either showing result if < 3s, or idle if >= 3s elapsed) |
| REJECTED scan with empty error message from server | Display a default message: "Scan rejected. Please see administration." instead of an empty string |

---

## Test Scenarios

- [ ] ACCEPTED ScanResult updates display with student name, grade/section, ID, scan type, and time on green background
- [ ] DUPLICATE ScanResult updates display with amber background and shows duplicate message
- [ ] REJECTED ScanResult updates display with red background and shows server error message
- [ ] QUEUED ScanResult updates display with blue background and "Scan queued (offline)" message
- [ ] Idle state shows gray background with "Ready to Scan" message and no student info fields
- [ ] Auto-clear timer resets display to idle after 3 seconds (use fake timer/Task.Delay mock)
- [ ] New scan arriving before auto-clear cancels previous timer and starts new 3-second timer
- [ ] ScanStatusToColorConverter returns correct Color for each ScanStatus enum value
- [ ] Missing grade field (null) displays "---" instead of null text
- [ ] Missing section field (null) displays "---" instead of null text
- [ ] REJECTED scan with null/empty message displays default rejection message
- [ ] StudentName with 50+ characters is handled without layout overflow (integration/UI test)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0010 | Requires | IScanApiService returning ScanResult model with Status, student info, and error fields | Draft |
| US0009 | Requires | CurrentScanType from MainViewModel for displaying scan type in result area | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| CommunityToolkit.Mvvm | NuGet Package | Available; provides [ObservableProperty] for ViewModel bindings |
| CommunityToolkit.Maui | NuGet Package | Available; may provide StatusToColorConverter or other helpers |
| .NET MAUI XAML | Platform SDK | Available in .NET 8.0 MAUI |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

---

## Open Questions

- [ ] Should the auto-clear duration (3 seconds) be configurable via a hidden setting for future flexibility, or strictly hardcoded? - Owner: Product
- [ ] For REJECTED scans, should the student name still be shown if the server happens to return it (e.g., for "StudentInactive"), or should rejected scans always hide student info? - Owner: UX
- [ ] Should the result area have a subtle transition/fade animation, or should changes be instantaneous for maximum clarity? - Owner: UX
- [ ] Should Guard Gary be able to tap the result area to dismiss it early (before 3 seconds), or is auto-clear the only mechanism? - Owner: Product

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
