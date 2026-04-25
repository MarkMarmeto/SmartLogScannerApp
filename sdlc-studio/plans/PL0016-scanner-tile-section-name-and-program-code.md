# PL0016: Scanner Tile — Fix Section Name Trimming, Show Program Code

> **Status:** Draft
> **Story:** [US0091: Scanner Tile — Fix Section Name Trimming, Show Program Code](../stories/US0091-scanner-section-name-trim-and-program-code.md)
> **Epic:** EP0011: Multi-Camera Scanning / Scan Feedback
> **Created:** 2026-04-25
> **Language:** C# 12 / .NET MAUI 8.0 XAML

## Overview

Two UI improvements to the student scan feedback tile: (1) remove any character-limit / text-truncation that clips Section names, and (2) add Program Code to the Grade/Section display line. The scan API already returns Program; this is a binding + layout change only. The tile must remain readable at all supported camera grid sizes (1, 2, 4, 6, 8 cameras).

---

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Full Section name | No truncation or ellipsis on Section; wraps if needed |
| AC2 | Program Code displayed | Tile shows "Grade N · ProgramCode · SectionName" |
| AC3 | Responsive wrapping | At 4-camera width, Grade/Program/Section wraps cleanly; font stays readable |
| AC4 | Fallback | Student with null Program → tile shows "Grade N · SectionName" |
| AC5 | Visitor/rejection tiles unaffected | Only student tile layout changed |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET MAUI 8.0 XAML
- **Architecture:** MVVM; `CameraSlotViewModel` (or `CameraSlotState`) binds to the tile DataTemplate
- **Test Framework:** xUnit (logic); manual visual smoke-test for layout

### Key Existing Files
- **Tile DataTemplate:** likely in `SmartLog.Scanner/Pages/MainPage.xaml` or a `CameraSlot.xaml` resource — find the `<DataTemplate>` or `<ContentView>` that renders the student scan result
- **`CameraSlotViewModel`** (or `CameraSlotState`): exposes `StudentName`, `GradeLevel`, `SectionName` properties — add `ProgramCode`
- **Scan API response DTO:** `ScanApiService` response already includes Program per US0090/scan API — confirm `ProgramCode` field is deserialised
- **Truncation source:** look for `LineBreakMode="TailTruncation"` or a fixed `WidthRequest` / `MaxLength` on the Section label

---

## Recommended Approach

**Strategy:** Test-After  
**Rationale:** XAML layout + binding addition; no algorithmic complexity. Manual visual test at 1/2/4/6/8 camera grid. Unit test for ViewModel binding.

---

## Implementation Phases

### Phase 1: ViewModel — ProgramCode Binding

**Goal:** Expose `ProgramCode` from the scan API response on `CameraSlotViewModel`.

- [ ] In `CameraSlotViewModel.cs` (or `CameraSlotState.cs`), add:
  ```csharp
  [ObservableProperty]
  private string? _programCode;
  ```
- [ ] In the method that sets tile data from the scan response, populate:
  ```csharp
  ProgramCode = response.Program;  // nullable — null if API field absent
  ```
- [ ] Confirm `ScanApiService` response DTO has a `Program` property; if it uses a different name (e.g. `ProgramCode`), map it here.

**Files:** `SmartLog.Scanner.Core/ViewModels/CameraSlotViewModel.cs` (or equivalent), `SmartLog.Scanner.Core/Services/ScanApiService.cs` (verify DTO only)

### Phase 2: XAML — Remove Truncation

**Goal:** Identify and remove any Section label truncation.

- [ ] Search for `LineBreakMode="TailTruncation"` or `MaxLength` on the Section label in the tile DataTemplate.
- [ ] Change `LineBreakMode` to `WordWrap` on the Section label; remove any fixed `MaxLength`.
- [ ] Remove any hardcoded `WidthRequest` or `MaxWidth` that would clip the label.

**Files:** `SmartLog.Scanner/Pages/MainPage.xaml` (or tile DataTemplate file)

### Phase 3: XAML — Add Program Code to Grade/Section Row

**Goal:** Render "Grade N · ProgramCode · SectionName" with graceful fallback.

- [ ] In the tile DataTemplate, replace the existing Grade/Section display with a `FlexLayout` or a `HorizontalStackLayout` with wrap:
  ```xml
  <FlexLayout Wrap="Wrap" Direction="Row" AlignItems="Start">
      <Label Text="{Binding GradeLevelLabel}" FontSize="13" />
      <Label Text=" · " FontSize="13" IsVisible="{Binding ProgramCode, Converter={StaticResource NotNullConverter}}" />
      <Label Text="{Binding ProgramCode}" FontSize="13" IsVisible="{Binding ProgramCode, Converter={StaticResource NotNullConverter}}" />
      <Label Text=" · " FontSize="13" />
      <Label Text="{Binding SectionName}" FontSize="13" LineBreakMode="WordWrap" />
  </FlexLayout>
  ```
  Or equivalently, bind to a formatted string property on the ViewModel:
  ```csharp
  public string GradeProgramSection => string.IsNullOrEmpty(ProgramCode)
      ? $"Grade {GradeLevel} · {SectionName}"
      : $"Grade {GradeLevel} · {ProgramCode} · {SectionName}";
  ```
- [ ] Choose the simpler approach (single computed binding vs. multi-label FlexLayout) based on how complex the existing tile is.
- [ ] Ensure visitor tile (`<DataTemplate x:Key="VisitorScanTemplate">` or similar) is a separate template and is not affected.
- [ ] Ensure rejection tile is a separate template and is not affected.

**Files:** `SmartLog.Scanner/Pages/MainPage.xaml` (or tile DataTemplate file)

### Phase 4: Styles

**Goal:** Ensure the label styles are consistent and readable at all grid sizes.

- [ ] Confirm no style resource sets a fixed height or line clamp on the Grade/Section label row.
- [ ] If needed, add a `GradeProgramSectionLabel` style in `Styles.xaml` with `LineBreakMode="WordWrap"`.
- [ ] Manual visual test: run the app with 1, 2, 4 camera slots; scan a student with a long section name (e.g. "STEM-Aquinas"); verify full name displays.

**Files:** `SmartLog.Scanner/Resources/Styles/Styles.xaml`

### Phase 5: Tests

| AC | Test | File |
|----|------|------|
| AC2 | `CameraSlotViewModel.GradeProgramSection` includes ProgramCode when set | `CameraSlotViewModelTests.cs` |
| AC4 | Fallback: ProgramCode null → "Grade N · SectionName" | same |

- [ ] Run `dotnet test`; confirm zero regressions.
- [ ] Manual smoke test: scan a student at 1, 2, 4 camera grid sizes; verify Section is full and Program appears.

---

## Edge Case Handling

| # | Edge Case | Handling |
|---|-----------|----------|
| 1 | Section name 40+ chars | Wraps to next line; readable; no ellipsis except on final overflow line |
| 2 | REGULAR program | Shown as "Grade 7 · REGULAR · 7-A" |
| 3 | Program field absent from API response | `ProgramCode` is null; fallback gracefully |
| 4 | Visitor or rejection scan | Uses separate tile DataTemplate; unaffected |

---

## Definition of Done

- [ ] `CameraSlotViewModel` exposes `ProgramCode` (or `GradeProgramSection` computed string)
- [ ] Section label truncation removed from tile DataTemplate
- [ ] Tile shows Grade · Program · Section; wraps at narrow grid widths
- [ ] Null Program falls back to Grade · Section
- [ ] Visitor and rejection tile layouts unchanged
- [ ] Unit tests passing; manual visual test passed at 1/2/4 camera grid

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-25 | Claude | Initial plan |
