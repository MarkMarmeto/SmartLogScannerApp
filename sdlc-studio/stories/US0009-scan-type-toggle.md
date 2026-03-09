# US0009: Implement Scan Type Toggle (ENTRY/EXIT)

> **Status:** Done
> **Epic:** [EP0003: Scan Processing and Feedback](../epics/EP0003-scan-processing-and-feedback.md)
> **Owner:** AI Assistant
> **Reviewer:** SDLC Studio
> **Created:** 2026-02-13
> **Completed:** 2026-02-16

## User Story

**As a** Guard Gary
**I want** a prominent toggle on the main scan page to switch between ENTRY and EXIT scan modes
**So that** I can quickly change the scan direction at shift changeover without navigating away from the scanning screen, and the mode is remembered across app restarts

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency. Needs dead-simple controls he can operate during peak hours without hesitation. "I just need it to beep green or beep red."
[Full persona details](../personas.md#guard-gary)

### Background
School gates operate in two modes: morning ENTRY and afternoon EXIT. Guard Gary needs to toggle this once per shift (or at shift changeover) so that every scan submitted to the server carries the correct scan direction. The toggle must be highly visible and always accessible -- Guard Gary should never need to navigate to a settings page to change it. The selected mode is persisted to Preferences so that if the app restarts (power outage, crash recovery), it resumes in the last-used mode. The scan type value is included in every POST /api/v1/scans request body as the `scanType` field.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | UX | High-contrast, large UI elements for Guard Gary | Toggle must use large text and high-contrast colors visible in bright outdoor conditions |
| TRD | Architecture | MVVM with CommunityToolkit.Mvvm; ObservableProperty for state | ScanType property on MainViewModel must be [ObservableProperty] with notification to UI |
| TRD | Architecture | DI-based service interfaces | Must use IPreferencesService (from US0001) for persistence, not direct Preferences calls |
| PRD | Feature | Scan type included in submission payload (F05/F11) | MainViewModel must expose current scan type for IScanApiService to include in request body |
| Epic | Performance | Scan-to-result < 500ms | Toggle change must be instantaneous; persistence must not block the UI thread |

---

## Acceptance Criteria

### AC1: Toggle control visible on MainPage
- **Given** the MainPage is displayed and the app is in "Ready to Scan" state
- **When** Guard Gary looks at the scan page
- **Then** a toggle control is visible showing the current mode as either "[ENTRY Mode]" or "[EXIT Mode]" in large text (minimum 20pt equivalent), positioned prominently near the top of the scan area, using high-contrast styling (white text on a distinct background color)

### AC2: Toggle switches between ENTRY and EXIT
- **Given** the toggle is currently showing "ENTRY Mode"
- **When** Guard Gary taps/clicks the toggle control
- **Then** the toggle switches to "EXIT Mode" and the visual label updates immediately (within one frame), and vice versa

### AC3: Current mode persisted to Preferences
- **Given** Guard Gary switches the toggle from "ENTRY" to "EXIT"
- **When** the toggle state changes
- **Then** IPreferencesService.SetDefaultScanType("EXIT") is called, persisting the value under the key "Scanner.DefaultScanType"

### AC4: App starts in last-used mode
- **Given** Guard Gary previously set the toggle to "EXIT" and then closed the app
- **When** the app is launched again
- **Then** MainViewModel reads IPreferencesService.GetDefaultScanType() during initialization and the toggle displays "EXIT Mode" without any user interaction

### AC5: First launch defaults to ENTRY
- **Given** no value has been previously saved for "Scanner.DefaultScanType" (first launch or preferences cleared)
- **When** the app starts
- **Then** IPreferencesService.GetDefaultScanType() returns "ENTRY" (the default), and the toggle displays "ENTRY Mode"

### AC6: Scan type value accessible for submission
- **Given** the toggle is set to "EXIT"
- **When** a scan is processed and submitted to the server via IScanApiService
- **Then** the scanType field in the POST /api/v1/scans request body is "EXIT"

### AC7: Toggle is always accessible during scanning
- **Given** the app is actively processing a scan (waiting for server response or displaying a result)
- **When** Guard Gary taps/clicks the toggle
- **Then** the toggle responds and changes mode; it is never disabled or hidden during any scan state

### AC8: Visual indicator uses high-contrast styling
- **Given** the toggle is displayed on MainPage
- **When** the mode is ENTRY
- **Then** the toggle uses a visually distinct style (e.g., entry-specific background color or icon) that is immediately distinguishable from EXIT mode at arm's length, with font weight Bold and minimum 20pt equivalent font size

---

## Scope

### In Scope
- Toggle control on MainPage (segmented button, switch, or large tappable button pair)
- MainViewModel [ObservableProperty] for CurrentScanType (string: "ENTRY" or "EXIT")
- [RelayCommand] for ToggleScanType
- Read from IPreferencesService.GetDefaultScanType() on ViewModel initialization
- Write to IPreferencesService.SetDefaultScanType() on toggle change
- Data binding from MainPage XAML to MainViewModel.CurrentScanType
- Visual styling for both states (high-contrast, large text)
- Unit tests for MainViewModel toggle logic

### Out of Scope
- Scan submission logic (covered by US0010)
- Audio feedback when toggling (no sound on toggle change)
- Toggle animation or transition effects
- Admin-only lock on toggle (any user can toggle freely)
- Schedule-based automatic toggling (e.g., auto-switch at noon)
- Additional scan types beyond ENTRY and EXIT

---

## Technical Notes

### Implementation Details
- **MainViewModel** exposes:
  ```csharp
  [ObservableProperty]
  private string _currentScanType = "ENTRY";

  [RelayCommand]
  private void ToggleScanType()
  {
      CurrentScanType = CurrentScanType == "ENTRY" ? "EXIT" : "ENTRY";
      _preferencesService.SetDefaultScanType(CurrentScanType);
  }
  ```
- On ViewModel construction or `InitializeAsync()`, read the persisted value:
  ```csharp
  CurrentScanType = _preferencesService.GetDefaultScanType(); // defaults to "ENTRY"
  ```
- **MainPage.xaml** binds the toggle label and command:
  ```xml
  <Button Text="{Binding CurrentScanType, StringFormat='{0} Mode'}"
          Command="{Binding ToggleScanTypeCommand}"
          Style="{StaticResource ScanTypeToggleStyle}" />
  ```
- Consider using a segmented control (two side-by-side buttons with active state highlighting) for clearer affordance than a single toggle button.
- Toggle style resources defined in AppStyles.xaml:
  ```xml
  <Style x:Key="ScanTypeToggleStyle" TargetType="Button">
      <Setter Property="FontSize" Value="24" />
      <Setter Property="FontAttributes" Value="Bold" />
      <Setter Property="MinimumHeightRequest" Value="60" />
  </Style>
  ```

### API Contracts
Not directly applicable. This story provides the `scanType` value consumed by US0010 (IScanApiService).

### Data Requirements

**Preference Key:**

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| Scanner.DefaultScanType | string | "ENTRY" | Current scan direction mode |

**ViewModel Properties:**

| Property | Type | Binding | Description |
|----------|------|---------|-------------|
| CurrentScanType | string | TwoWay | "ENTRY" or "EXIT"; bound to toggle UI |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Toggle tapped during active scan processing (server call in progress) | Toggle responds immediately; scan type changes for the next scan; the in-flight scan retains the type it was submitted with |
| Rapid toggling (multiple taps in quick succession) | Each tap persists the new value; final state is consistent between UI display and Preferences; no crash or deadlock |
| Preference read failure on startup (corrupted Preferences store) | IPreferencesService.GetDefaultScanType() returns the default "ENTRY"; error is logged; app starts normally in ENTRY mode |
| First launch with no saved preference | GetDefaultScanType() returns "ENTRY" (the coded default); toggle displays "ENTRY Mode" |
| Invalid value stored in Preferences (e.g., "LUNCH" from manual tampering) | GetDefaultScanType() should validate the returned value; if not "ENTRY" or "EXIT", default to "ENTRY" and overwrite the invalid preference |
| Preferences write failure (disk full, permissions) | Toggle UI still updates (in-memory state changes); error is logged; on next app restart, the old persisted value is restored (may be stale) |

---

## Test Scenarios

- [ ] MainViewModel initializes CurrentScanType to "ENTRY" when IPreferencesService returns default (no stored value)
- [ ] MainViewModel initializes CurrentScanType to "EXIT" when IPreferencesService.GetDefaultScanType() returns "EXIT"
- [ ] ToggleScanTypeCommand changes CurrentScanType from "ENTRY" to "EXIT"
- [ ] ToggleScanTypeCommand changes CurrentScanType from "EXIT" to "ENTRY"
- [ ] ToggleScanTypeCommand calls IPreferencesService.SetDefaultScanType with the new value after each toggle
- [ ] Rapid toggling (3 consecutive calls) results in consistent final state ("EXIT" -> "ENTRY" -> "EXIT") and 3 calls to SetDefaultScanType
- [ ] CurrentScanType property raises PropertyChanged notification on toggle (CommunityToolkit.Mvvm auto-generates this)
- [ ] Invalid preference value ("INVALID") is corrected to "ENTRY" on ViewModel initialization

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0001 | Requires | IPreferencesService interface and implementation for reading/writing "Scanner.DefaultScanType" | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| CommunityToolkit.Mvvm | NuGet Package | Available; provides [ObservableProperty] and [RelayCommand] |
| .NET MAUI Preferences API | Platform SDK | Available in .NET 8.0 MAUI (wrapped by IPreferencesService) |

---

## Estimation

**Story Points:** 3
**Complexity:** Low

---

## Open Questions

- [ ] Should the toggle be a segmented button pair (ENTRY | EXIT) or a single button that flips? Segmented control provides clearer affordance but takes more horizontal space. - Owner: UX
- [ ] Should toggling while a scan result is displayed immediately clear the result display, or leave the previous result visible until auto-clear? - Owner: Product
- [ ] Is there a need for an optional "Confirm toggle" dialog to prevent accidental mode changes during peak scanning? - Owner: Product

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
