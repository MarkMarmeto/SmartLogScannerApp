# PL0017: Scanner Header — Enlarge Date/Time, Anchor Left-Most

> **Status:** Draft
> **Story:** [US0092: Scanner Header — Enlarge Date/Time, Anchor Left-Most](../stories/US0092-scanner-datetime-prominent-leftmost.md)
> **Epic:** EP0011: Multi-Camera Scanning / Scan Feedback
> **Created:** 2026-04-25
> **Language:** C# 12 / .NET MAUI 8.0 XAML

## Overview

Relocate the date/time display to the left-most position in the scanner header/toolbar and enlarge it so it's readable at 2-metre glance distance. Time (HH:mm) is the dominant element; date ("Thu, 24 Apr 2026") is secondary. Clock ticks every second via the existing timer or a new one. No new data source required.

---

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | Left-most placement | Date/time is the left-most element in the header/toolbar |
| AC2 | Enlarged font | At minimum 1.5× previous size; readable from 2 metres on 14" display |
| AC3 | Two-line layout | Time (HH:mm) dominant; date above or below in smaller but readable weight |
| AC4 | Live-updating | Clock ticks without visible jank; matches system clock |
| AC5 | Locale/format | Date: "EEE, dd MMM yyyy"; Time: 24-hour "HH:mm" (en-PH) |
| AC6 | No regression | Toolbar controls still accessible at 1366×768 (common gate-PC resolution) |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 / .NET MAUI 8.0 XAML
- **Architecture:** MVVM; `MainViewModel` drives the clock via a `Timer` or `IDispatcherTimer`

### Key Existing Files
- **`MainPage.xaml`:** Header / toolbar `Grid` or `HorizontalStackLayout` — contains current date/time element, logo, status chips
- **`MainViewModel.cs`:** Likely already has `CurrentTime` and/or `CurrentDate` bindable properties; confirm and extend if needed
- **`Styles.xaml`:** Date/time label style — update sizes here

---

## Recommended Approach

**Strategy:** Test-After  
**Rationale:** Pure XAML + style change; clock logic likely already exists. Manual visual test for readability at distance. Unit test confirms format strings.

---

## Implementation Phases

### Phase 1: ViewModel — Clock Bindings

**Goal:** Ensure `MainViewModel` exposes separate `CurrentDate` and `CurrentTime` strings with the correct formats.

- [ ] In `MainViewModel.cs`, confirm or add:
  ```csharp
  [ObservableProperty] private string _currentTime = "";
  [ObservableProperty] private string _currentDate = "";

  // In the clock tick handler (timer interval: 1 second):
  private void OnClockTick(object? sender, EventArgs e) {
      var now = DateTime.Now;
      CurrentTime = now.ToString("HH:mm");
      CurrentDate = now.ToString("ddd, dd MMM yyyy");  // e.g. "Thu, 24 Apr 2026"
  }
  ```
- [ ] If a `DispatcherTimer` or `Timer` already drives a single `CurrentDateTime` string, replace with two separate formatted properties.
- [ ] Confirm the timer is started in `OnAppearing` / `OnNavigatedTo` and stopped in `OnDisappearing` / `OnNavigatedFrom` to avoid memory leaks.

**Files:** `SmartLog.Scanner/ViewModels/MainViewModel.cs`

### Phase 2: Header XAML Layout

**Goal:** Move date/time to the left-most position in the header.

- [ ] Open `MainPage.xaml`; locate the header container (likely a `<Grid>`, `<HorizontalStackLayout>`, or `<ToolbarItem>` area).
- [ ] Restructure so the date/time block is the first child (left-most):
  ```xml
  <!-- Left: Date/Time block -->
  <VerticalStackLayout Grid.Column="0" HorizontalOptions="Start" VerticalOptions="Center" Spacing="0">
      <Label Text="{Binding CurrentTime}"
             Style="{StaticResource HeaderTimeLabel}" />
      <Label Text="{Binding CurrentDate}"
             Style="{StaticResource HeaderDateLabel}" />
  </VerticalStackLayout>

  <!-- Center/Right: remaining toolbar elements (logo, status chips, menu) -->
  ...
  ```
- [ ] If the header uses a `Grid`, assign `ColumnDefinitions` so the date/time occupies the first column with `Auto` width; remaining columns fill the rest.
- [ ] Ensure no existing element (logo, hamburger, etc.) sits to the left of the date/time block.

**Files:** `SmartLog.Scanner/Pages/MainPage.xaml`

### Phase 3: Styles — Enlarged Typography

**Goal:** Define named styles for the date and time labels at the required sizes.

- [ ] In `Styles.xaml`, add (or update existing):
  ```xml
  <Style x:Key="HeaderTimeLabel" TargetType="Label">
      <Setter Property="FontSize" Value="28" />
      <Setter Property="FontAttributes" Value="Bold" />
      <Setter Property="TextColor" Value="{AppThemeBinding Light={StaticResource Gray900}, Dark={StaticResource White}}" />
  </Style>

  <Style x:Key="HeaderDateLabel" TargetType="Label">
      <Setter Property="FontSize" Value="13" />
      <Setter Property="FontAttributes" Value="None" />
      <Setter Property="TextColor" Value="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray300}}" />
  </Style>
  ```
- [ ] Validate that `28` is at minimum 1.5× the previous size (check existing size; adjust if needed). If the previous time was `18`, 1.5× = `27` → `28` is fine.
- [ ] Test at 1366×768: verify the toolbar doesn't collapse or wrap other controls out of reach.

**Files:** `SmartLog.Scanner/Resources/Styles/Styles.xaml`

### Phase 4: Manual Visual Test

- [ ] Run on Windows gate PC at 1366×768 resolution.
- [ ] Verify time is the left-most element.
- [ ] Verify time label is visibly larger (≥1.5×).
- [ ] Verify clock ticks every second without jank.
- [ ] Verify format: time "14:32", date "Thu, 24 Apr 2026".
- [ ] Verify other toolbar items remain reachable at 1366×768.

### Phase 5: Tests

| AC | Test | File |
|----|------|------|
| AC5 | `CurrentTime` format is "HH:mm" | `MainViewModelTests.cs` |
| AC5 | `CurrentDate` format is "ddd, dd MMM yyyy" | same |
| AC4 | Timer updates both properties on tick | same |

- [ ] Run `dotnet test`; confirm zero regressions.

---

## Edge Case Handling

| # | Edge Case | Handling |
|---|-----------|----------|
| 1 | Display width < 1000px | Date wraps below time; toolbar controls clip gracefully; no horizontal scroll |
| 2 | App backgrounded / screen off | Timer paused or continues (MAUI platform-dependent); verify clock is current when app resumes |
| 3 | System clock changes mid-session | Next tick reflects updated system time; no special handling needed |

---

## Definition of Done

- [ ] `MainViewModel` exposes separate `CurrentTime` ("HH:mm") and `CurrentDate` ("ddd, dd MMM yyyy") properties
- [ ] Date/time block is the left-most header element in `MainPage.xaml`
- [ ] `HeaderTimeLabel` style is ≥1.5× the previous time label size
- [ ] Clock ticks live without jank
- [ ] Toolbar remains accessible at 1366×768
- [ ] Unit tests passing; `dotnet test` clean

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-04-25 | Claude | Initial plan |
