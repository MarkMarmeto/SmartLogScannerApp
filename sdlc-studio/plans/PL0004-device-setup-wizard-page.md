# PL0004: Device Setup Wizard Page - Implementation Plan

> **Status:** Completed
> **Story:** [US0004: Build Device Setup Wizard Page](../stories/US0004-device-setup-wizard-page.md)
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Created:** 2026-02-14
> **Language:** C# + XAML

## Overview

This plan implements the first-launch setup wizard for SmartLog Scanner. IT Admin Ian enters server URL, API key, HMAC secret, scan mode, and scan type. The form validates inputs, stores credentials securely via ISecureConfigService, saves settings via IPreferencesService, and sets Setup.Completed flag. Shell navigation guard ensures SetupPage shows on first launch and MainPage shows on subsequent launches.

The implementation uses **TDD** approach because:
1. SetupViewModel has clear business logic (validation, save) that's testable in isolation
2. MVVM separation enables pure unit tests for ViewModel without UI dependencies
3. Validation rules are well-defined with specific error messages
4. Save operation has 6 sequential steps that should be verified

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | SetupPage on first launch | Setup.Completed=false routes to SetupPage, not MainPage |
| AC2 | Form fields present | 5 fields: Server URL, API Key, HMAC Secret, Scan Mode picker, Scan Type picker |
| AC3 | Server URL validation | Must be valid http/https URL; inline error if invalid |
| AC4 | Required field validation | API Key and HMAC Secret required; inline error if empty |
| AC5 | Successful save | 6 storage calls in sequence, then navigate to MainPage |
| AC6 | Navigation guard | Setup.Completed=true bypasses SetupPage, goes to MainPage |
| AC7 | Save failure error | Error banner, Setup.Completed stays false, values preserved |
| AC8 | MVVM architecture | ViewModel with [ObservableProperty], [RelayCommand], no logic in code-behind |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12 + XAML
- **Framework:** .NET 8.0 MAUI (MacCatalyst + Windows)
- **Test Framework:** xUnit 2.6.4 + Moq 4.20.70
- **MVVM:** CommunityToolkit.Mvvm 8.2.2 (source generators)

### Relevant Best Practices
- MVVM pattern: View (XAML) → ViewModel (logic) → Model (services)
- ViewModels registered in DI container as transient
- Data binding with {Binding PropertyName} in XAML
- Source generators: [ObservableProperty], [RelayCommand]
- Shell navigation: GoToAsync("//routeName")
- Input validation: Set error properties, bind to error labels

### Library Documentation (Context7)

| Library | Key Patterns |
|---------|--------------|
| CommunityToolkit.Mvvm | `[ObservableProperty]` generates property change notifications; `[RelayCommand]` generates ICommand implementations |
| MAUI Shell | `Routing.RegisterRoute("route", typeof(Page))` registers routes; `Shell.Current.GoToAsync("//route")` navigates |
| MAUI Data Binding | `{Binding PropertyName}` one-way; `{Binding PropertyName, Mode=TwoWay}` two-way |
| MAUI Entry | `Keyboard="Url"` for URL input; `IsPassword="True"` for masked input |
| MAUI Picker | `ItemsSource="{Binding Options}"` + `SelectedItem="{Binding Selection}"` |

### Existing Patterns
- **Services**: ISecureConfigService and IPreferencesService already implemented (US0001)
- **Shell**: AppShell.xaml already exists with SetupPage and MainPage placeholders
- **ViewModels**: None yet (this is first ViewModel)
- **Views**: SetupPage and MainPage placeholders exist, need full implementation

---

## Recommended Approach

**Strategy:** TDD (Test-Driven Development)
**Rationale:**
- SetupViewModel has clear business logic: validation rules, save orchestration
- ViewModel can be tested in isolation with mocked services
- Validation rules have specific error messages to verify
- Save operation has defined sequence of 6 calls
- UI (XAML) is simple data binding to ViewModel properties

### Test Priority
1. **Validation tests**: URL format, required fields, error messages
2. **Save success test**: Verify all 6 service calls in correct order
3. **Save failure test**: Verify error handling, Setup.Completed not set
4. **Navigation test**: Verify GoToAsync called after successful save

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Create SetupViewModel with properties | `ViewModels/SetupViewModel.cs` | None | [ ] |
| 2 | Write SetupViewModel validation tests | `Tests/ViewModels/SetupViewModelTests.cs` | Task 1 | [ ] |
| 3 | Implement validation logic in SetupViewModel | `ViewModels/SetupViewModel.cs` | Task 2 | [ ] |
| 4 | Write SetupViewModel save tests | `Tests/ViewModels/SetupViewModelTests.cs` | Task 3 | [ ] |
| 5 | Implement save logic in SetupViewModel | `ViewModels/SetupViewModel.cs` | Task 4 | [ ] |
| 6 | Register SetupViewModel in DI container | `MauiProgram.cs` | Task 5 | [ ] |
| 7 | Create SetupPage.xaml UI | `Views/SetupPage.xaml` | Task 6 | [ ] |
| 8 | Update SetupPage.xaml.cs code-behind | `Views/SetupPage.xaml.cs` | Task 7 | [ ] |
| 9 | Update AppShell navigation guard | `AppShell.xaml.cs` | Task 8 | [ ] |
| 10 | Update AppShell routes | `AppShell.xaml` | Task 9 | [ ] |
| 11 | Test navigation flow manually | Manual test | Task 10 | [ ] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| Group 1 | Task 1 | None |
| Group 2 | Tasks 2-5 | Task 1 (TDD cycle: test → implement → test → implement) |
| Group 3 | Task 6 | Group 2 |
| Group 4 | Tasks 7-10 | Group 3 (sequential: ViewModel → View → Shell) |
| Group 5 | Task 11 | Group 4 (manual verification) |

---

## Implementation Phases

### Phase 1: SetupViewModel Structure (Task 1)
**Goal:** Create ViewModel with all properties and commands

- [ ] Create `ViewModels/SetupViewModel.cs`:
  ```csharp
  using CommunityToolkit.Mvvm.ComponentModel;
  using CommunityToolkit.Mvvm.Input;

  public partial class SetupViewModel : ObservableObject
  {
      private readonly ISecureConfigService _secureConfig;
      private readonly IPreferencesService _preferences;

      // Form field properties
      [ObservableProperty] private string _serverUrl = string.Empty;
      [ObservableProperty] private string _apiKey = string.Empty;
      [ObservableProperty] private string _hmacSecret = string.Empty;
      [ObservableProperty] private string _selectedScanMode = "USB";
      [ObservableProperty] private string _selectedScanType = "ENTRY";

      // Error properties
      [ObservableProperty] private string? _serverUrlError;
      [ObservableProperty] private string? _apiKeyError;
      [ObservableProperty] private string? _hmacSecretError;
      [ObservableProperty] private string? _saveError;

      // UI state
      [ObservableProperty] private bool _isSaving;

      // Picker options
      public List<string> ScanModeOptions { get; } = new() { "USB", "Camera" };
      public List<string> ScanTypeOptions { get; } = new() { "ENTRY", "EXIT" };

      public SetupViewModel(ISecureConfigService secureConfig, IPreferencesService preferences)
      {
          _secureConfig = secureConfig;
          _preferences = preferences;
      }

      [RelayCommand]
      private async Task SaveAsync()
      {
          // To be implemented in Phase 3
      }

      private bool ValidateAll()
      {
          // To be implemented in Phase 2
      }
  }
  ```

**Files:**
- `SmartLog.Scanner/ViewModels/SetupViewModel.cs` - New ViewModel class

### Phase 2: TDD - Validation Logic (Tasks 2-3)
**Goal:** Write failing validation tests, then implement to pass

**Test cases (SetupViewModelTests.cs):**
- [ ] ValidateAll_EmptyServerUrl_SetsServerUrlError
- [ ] ValidateAll_InvalidServerUrl_MissingScheme_SetsServerUrlError
- [ ] ValidateAll_InvalidServerUrl_NotAUrl_SetsServerUrlError
- [ ] ValidateAll_ValidHttpsUrl_ClearsServerUrlError
- [ ] ValidateAll_ValidHttpUrl_ClearsServerUrlError
- [ ] ValidateAll_EmptyApiKey_SetsApiKeyError
- [ ] ValidateAll_EmptyHmacSecret_SetsHmacSecretError
- [ ] ValidateAll_AllFieldsValid_ReturnsTrue_ClearsAllErrors
- [ ] ValidateAll_AnyFieldInvalid_ReturnsFalse

**Implementation (SetupViewModel.cs):**
```csharp
private bool ValidateAll()
{
    bool isValid = true;

    // AC3: Validate Server URL
    if (string.IsNullOrWhiteSpace(ServerUrl))
    {
        ServerUrlError = "This field is required";
        isValid = false;
    }
    else if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        ServerUrlError = "Please enter a valid URL (e.g., https://192.168.1.100:8443)";
        isValid = false;
    }
    else
    {
        ServerUrlError = null;
    }

    // AC4: Validate API Key (required)
    if (string.IsNullOrWhiteSpace(ApiKey))
    {
        ApiKeyError = "This field is required";
        isValid = false;
    }
    else
    {
        ApiKeyError = null;
    }

    // AC4: Validate HMAC Secret (required)
    if (string.IsNullOrWhiteSpace(HmacSecret))
    {
        HmacSecretError = "This field is required";
        isValid = false;
    }
    else
    {
        HmacSecretError = null;
    }

    return isValid;
}
```

**Files:**
- `SmartLog.Scanner.Tests/ViewModels/SetupViewModelTests.cs` - New test class (9 tests)
- `SmartLog.Scanner/ViewModels/SetupViewModel.cs` - Implement ValidateAll()

### Phase 3: TDD - Save Logic (Tasks 4-5)
**Goal:** Write failing save tests, then implement to pass

**Test cases (SetupViewModelTests.cs):**
- [ ] SaveAsync_InvalidInputs_DoesNotCallServices_StaysOnPage
- [ ] SaveAsync_ValidInputs_CallsAllServices_InCorrectOrder
- [ ] SaveAsync_ValidInputs_SetsSetupCompletedTrue
- [ ] SaveAsync_ValidInputs_NavigatesToMainPage (mock Shell.Current)
- [ ] SaveAsync_ServiceThrowsException_ShowsErrorBanner_DoesNotSetSetupCompleted
- [ ] SaveAsync_ServiceThrowsException_PreservesEnteredValues
- [ ] SaveAsync_SetsIsSavingTrue_DuringSave_ThenFalse

**Implementation (SetupViewModel.cs):**
```csharp
[RelayCommand]
private async Task SaveAsync()
{
    // Clear previous error
    SaveError = null;

    // Validate all fields
    if (!ValidateAll())
    {
        return; // Stay on page, errors shown inline
    }

    IsSaving = true;

    try
    {
        // AC5: Store credentials and settings in order
        await _secureConfig.SetApiKeyAsync(ApiKey);
        await _secureConfig.SetHmacSecretAsync(HmacSecret);
        _preferences.SetServerBaseUrl(ServerUrl);
        _preferences.SetScanMode(SelectedScanMode);
        _preferences.SetDefaultScanType(SelectedScanType);
        _preferences.SetSetupCompleted(true);

        // Navigate to main page
        await Shell.Current.GoToAsync("//main");
    }
    catch (Exception ex)
    {
        // AC7: Show error, don't set Setup.Completed, preserve values
        SaveError = $"Failed to save configuration: {ex.Message}. Please try again.";
        Log.Error(ex, "Setup save failed");
    }
    finally
    {
        IsSaving = false;
    }
}
```

**Files:**
- `SmartLog.Scanner.Tests/ViewModels/SetupViewModelTests.cs` - Add 7 save tests
- `SmartLog.Scanner/ViewModels/SetupViewModel.cs` - Implement SaveAsync()

### Phase 4: DI Registration (Task 6)
**Goal:** Register SetupViewModel in DI container

- [ ] Add to `MauiProgram.cs` after service registrations:
  ```csharp
  // US0004: Register ViewModels
  builder.Services.AddTransient<ViewModels.SetupViewModel>();
  ```

**Files:**
- `MauiProgram.cs` - Add ViewModel registration

### Phase 5: SetupPage UI (Tasks 7-8)
**Goal:** Create XAML form with data binding

- [ ] Replace `Views/SetupPage.xaml` content:
  ```xml
  <?xml version="1.0" encoding="utf-8" ?>
  <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
               xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
               xmlns:vm="clr-namespace:SmartLog.Scanner.ViewModels"
               x:Class="SmartLog.Scanner.Views.SetupPage"
               x:DataType="vm:SetupViewModel"
               Title="Device Setup">

      <ScrollView>
          <VerticalStackLayout Padding="20" Spacing="15">

              <!-- Page Header -->
              <Label Text="SmartLog Scanner" FontSize="24" FontAttributes="Bold" />
              <Label Text="Device Setup" FontSize="16" TextColor="{StaticResource Gray600}" />

              <!-- Save Error Banner (AC7) -->
              <Border IsVisible="{Binding SaveError, Converter={StaticResource StringNotNullOrEmptyConverter}}"
                      BackgroundColor="{StaticResource RejectedRed}"
                      Padding="10" Margin="0,10">
                  <Label Text="{Binding SaveError}" TextColor="White" />
              </Border>

              <!-- Server URL (AC2, AC3) -->
              <Label Text="Server URL" FontAttributes="Bold" />
              <Entry Text="{Binding ServerUrl, Mode=TwoWay}"
                     Placeholder="https://192.168.1.100:8443"
                     Keyboard="Url" />
              <Label Text="{Binding ServerUrlError}"
                     IsVisible="{Binding ServerUrlError, Converter={StaticResource StringNotNullOrEmptyConverter}}"
                     TextColor="{StaticResource RejectedRed}"
                     FontSize="12" />

              <!-- API Key (AC2, AC4) -->
              <Label Text="API Key" FontAttributes="Bold" />
              <Entry Text="{Binding ApiKey, Mode=TwoWay}"
                     Placeholder="Enter device API key"
                     IsPassword="True" />
              <Label Text="{Binding ApiKeyError}"
                     IsVisible="{Binding ApiKeyError, Converter={StaticResource StringNotNullOrEmptyConverter}}"
                     TextColor="{StaticResource RejectedRed}"
                     FontSize="12" />

              <!-- HMAC Secret (AC2, AC4) -->
              <Label Text="HMAC Secret" FontAttributes="Bold" />
              <Entry Text="{Binding HmacSecret, Mode=TwoWay}"
                     Placeholder="Enter HMAC secret key"
                     IsPassword="True" />
              <Label Text="{Binding HmacSecretError}"
                     IsVisible="{Binding HmacSecretError, Converter={StaticResource StringNotNullOrEmptyConverter}}"
                     TextColor="{StaticResource RejectedRed}"
                     FontSize="12" />

              <!-- Scan Mode (AC2) -->
              <Label Text="Scan Mode" FontAttributes="Bold" />
              <Picker ItemsSource="{Binding ScanModeOptions}"
                      SelectedItem="{Binding SelectedScanMode, Mode=TwoWay}" />

              <!-- Default Scan Type (AC2) -->
              <Label Text="Default Scan Type" FontAttributes="Bold" />
              <Picker ItemsSource="{Binding ScanTypeOptions}"
                      SelectedItem="{Binding SelectedScanType, Mode=TwoWay}" />

              <!-- Save Button -->
              <Button Text="Save Configuration"
                      Command="{Binding SaveCommand}"
                      IsEnabled="{Binding IsSaving, Converter={StaticResource InvertedBoolConverter}}"
                      Margin="0,20,0,0" />

          </VerticalStackLayout>
      </ScrollView>
  </ContentPage>
  ```

- [ ] Update `Views/SetupPage.xaml.cs` code-behind:
  ```csharp
  public partial class SetupPage : ContentPage
  {
      public SetupPage(SetupViewModel viewModel)
      {
          InitializeComponent();
          BindingContext = viewModel;
      }
  }
  ```

**Files:**
- `SmartLog.Scanner/Views/SetupPage.xaml` - XAML UI
- `SmartLog.Scanner/Views/SetupPage.xaml.cs` - Code-behind with DI injection

### Phase 6: Shell Navigation (Tasks 9-10)
**Goal:** Configure routes and navigation guard

- [ ] Update `AppShell.xaml`:
  ```xml
  <Shell ...>
      <ShellContent Route="setup" ContentTemplate="{DataTemplate views:SetupPage}" />
      <ShellContent Route="main" ContentTemplate="{DataTemplate views:MainPage}" />
  </Shell>
  ```

- [ ] Update `AppShell.xaml.cs` with navigation guard:
  ```csharp
  public partial class AppShell : Shell
  {
      public AppShell()
      {
          InitializeComponent();
      }

      protected override void OnNavigated(ShellNavigatedEventArgs args)
      {
          base.OnNavigated(args);

          // AC1/AC6: Navigation guard based on Setup.Completed
          var preferences = Handler.MauiContext.Services.GetRequiredService<IPreferencesService>();
          if (!preferences.GetSetupCompleted() && Current.CurrentState.Location.OriginalString != "//setup")
          {
              Dispatcher.Dispatch(async () =>
              {
                  await GoToAsync("//setup");
              });
          }
      }
  }
  ```

**Files:**
- `SmartLog.Scanner/AppShell.xaml` - Add route definitions
- `SmartLog.Scanner/AppShell.xaml.cs` - Add navigation guard

### Phase 7: Testing & Validation
**Goal:** Verify all acceptance criteria

| AC | Verification Method | File Evidence | Status |
|----|---------------------|---------------|--------|
| AC1 | Manual test: Fresh install shows SetupPage | AppShell.xaml.cs navigation guard | Pending |
| AC2 | Manual test: All 5 fields visible | SetupPage.xaml lines 20-60 | Pending |
| AC3 | Unit tests: URL validation | SetupViewModelTests.cs (4 tests) | Pending |
| AC4 | Unit tests: Required field validation | SetupViewModelTests.cs (2 tests) | Pending |
| AC5 | Unit test: Save calls all 6 services | SetupViewModelTests.cs SaveAsync_ValidInputs test | Pending |
| AC6 | Manual test: After setup, app shows MainPage | AppShell.xaml.cs navigation guard | Pending |
| AC7 | Unit test: Save failure shows error | SetupViewModelTests.cs SaveAsync_ServiceThrowsException test | Pending |
| AC8 | Code review: MVVM pattern | SetupViewModel uses [ObservableProperty], no logic in code-behind | Pending |

---

## Edge Case Handling

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | All fields empty and Save pressed | ValidateAll returns false; inline errors shown for URL, API key, HMAC secret | Phase 2 |
| 2 | Malformed URL: "not-a-url" | Uri.TryCreate fails; ServerUrlError set with message | Phase 2 |
| 3 | URL without scheme: "192.168.1.100:8443" | Uri.TryCreate succeeds but scheme check fails; error set | Phase 2 |
| 4 | URL with trailing slash | Accepted as valid; stored as entered | Phase 2 |
| 5 | Special characters in API key | No validation restrictions; stored via SecureStorage | Phase 3 |
| 6 | Very long API key | No length limit enforced; stored as-is | Phase 3 |
| 7 | Back navigation during setup | Navigation guard re-routes to SetupPage | Phase 6 |
| 8 | App killed before Save completes | Setup.Completed not set (atomic operation); setup shown on re-launch | Phase 3 |
| 9 | SecureStorage unavailable | Exception caught; SaveError banner shown; Setup.Completed not set | Phase 3 |
| 10 | Picker defaults not changed | Default values "USB" and "ENTRY" used and stored | Phase 1 |

**Coverage:** 10/10 edge cases handled

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Shell navigation timing issues | Navigation guard might not fire reliably | Use Dispatcher.Dispatch for thread-safe navigation |
| MAUI binding converters missing | StringNotNullOrEmptyConverter not available | Define custom converters in App.xaml or use IsVisible binding to bool properties |
| ViewModel DI injection in XAML | SetupPage might not resolve ViewModel | Register ViewModel in DI, inject via constructor, set as BindingContext |
| Testing Shell.Current.GoToAsync | Static Shell API hard to mock | Create INavigationService abstraction or use integration tests |
| Password field visibility | IT Admin Ian might mistype credentials | Accept as limitation for v1.0 (toggle is out of scope) |

---

## Definition of Done

- [ ] All acceptance criteria implemented (AC1-AC8)
- [ ] SetupViewModel with 16 unit tests (9 validation + 7 save logic)
- [ ] SetupPage.xaml with complete form UI
- [ ] AppShell navigation guard implemented
- [ ] ViewModel registered in DI container
- [ ] Manual tests pass:
  - Fresh install shows SetupPage
  - Successful save navigates to MainPage
  - Re-launch shows MainPage (Setup.Completed persisted)
- [ ] Code follows MVVM pattern (no logic in code-behind)
- [ ] Build successful

---

## Notes

**Why TDD:**
SetupViewModel contains pure business logic (validation, save orchestration) that's easily testable in isolation with mocked services. Writing tests first ensures validation rules are correct and save flow is verified before UI implementation.

**Converters Needed:**
- `StringNotNullOrEmptyConverter` for error visibility
- `InvertedBoolConverter` for button disable during save

These can be defined in App.xaml Resources or as inline code.

**Navigation Testing:**
Shell.Current.GoToAsync is static, making it hard to mock. Options:
1. Create INavigationService abstraction (recommended for production)
2. Accept integration testing for navigation (faster for v1.0)

This plan uses Option 2 for pragmatism. Can refactor to Option 1 later if needed.

**Future Enhancement:**
US0005 will add "Test Connection" button to this page, which will use the HttpClient from US0002 to validate credentials before saving.
