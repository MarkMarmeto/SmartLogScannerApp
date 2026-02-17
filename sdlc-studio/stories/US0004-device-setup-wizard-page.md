# US0004: Build Device Setup Wizard Page

> **Status:** Done
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As** IT Admin Ian
**I want** a first-launch setup wizard that collects the server URL, API key, HMAC secret, scan mode, and default scan type
**So that** I can configure each gate scanner device once with valid credentials and settings, and the device is ready for Guard Gary to use without further configuration

## Context

### Persona Reference
**IT Admin Ian** - School IT administrator, intermediate technical proficiency, configures 3-8 scanner devices across the school campus. Needs a straightforward one-time setup process with clear field labels and validation.
[Full persona details](../personas.md#it-admin-ian)

### Background
When SmartLog Scanner is installed on a new gate machine, it has no configuration. IT Admin Ian runs the app for the first time and must enter the server URL (from the school server), API key (generated in the SmartLog Admin Web App device registration), HMAC secret (provided by the admin panel), scan mode (Camera or USB depending on the hardware at this gate), and default scan type (ENTRY or EXIT depending on the gate's direction). The setup wizard must validate inputs, securely store credentials via ISecureConfigService, persist settings via IPreferencesService, and set the Setup.Completed flag so subsequent launches skip setup and go directly to the main scanning page. Shell navigation guards enforce this routing.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Security | Credentials stored via MAUI SecureStorage, never in plain text | SetupPage save must call ISecureConfigService for API key and HMAC secret |
| PRD | UX | Clear error messages on failure; user can retry | Validation errors shown inline per field; save errors shown as banner |
| TRD | Architecture | MVVM with CommunityToolkit.Mvvm | SetupPage binds to SetupViewModel; logic in ViewModel, not code-behind |
| TRD | Architecture | Shell navigation for page routing | AppShell routes configured for SetupPage and MainPage with navigation guard |
| PRD | Config | Setup.Completed preference flag guards navigation | IPreferencesService.GetSetupCompleted() checked at app start |

---

## Acceptance Criteria

### AC1: SetupPage shown on first launch
- **Given** the application is launched for the first time (Setup.Completed = false in Preferences)
- **When** Shell navigation evaluates the startup route
- **Then** the user is navigated to SetupPage instead of MainPage, and there is no way to navigate to MainPage without completing setup

### AC2: Form fields present and correctly typed
- **Given** SetupPage is displayed
- **When** IT Admin Ian views the form
- **Then** the following fields are present:
  - Server URL: text input with placeholder "https://192.168.1.100:8443" and keyboard type URL
  - API Key: password input (masked characters) with placeholder "Enter device API key"
  - HMAC Secret: password input (masked characters) with placeholder "Enter HMAC secret key"
  - Scan Mode: picker with options "Camera" and "USB" (default: "USB")
  - Default Scan Type: picker with options "ENTRY" and "EXIT" (default: "ENTRY")

### AC3: Server URL validation
- **Given** IT Admin Ian enters a value in the Server URL field
- **When** the field loses focus or the Save button is pressed
- **Then** the URL is validated: it must start with "http://" or "https://", must be a valid URI (parseable by Uri.TryCreate with UriKind.Absolute), and must have a scheme of http or https. If invalid, an inline error message "Please enter a valid URL (e.g., https://192.168.1.100:8443)" is displayed below the field in red text

### AC4: API key and HMAC secret validation
- **Given** IT Admin Ian leaves the API Key or HMAC Secret field empty
- **When** the Save button is pressed
- **Then** an inline error message "This field is required" is displayed below the empty field in red text, and the save operation is not executed

### AC5: Successful save stores credentials and settings
- **Given** all form fields are filled with valid values (server URL = "https://192.168.1.100:8443", API key = "sk-device-001-abc123", HMAC secret = "K7gNU3sdo+OL0w==", scan mode = "Camera", default scan type = "ENTRY")
- **When** IT Admin Ian presses the Save button
- **Then** the following operations occur in order:
  1. ISecureConfigService.SetApiKeyAsync("sk-device-001-abc123") is called
  2. ISecureConfigService.SetHmacSecretAsync("K7gNU3sdo+OL0w==") is called
  3. IPreferencesService.SetServerBaseUrl("https://192.168.1.100:8443") is called
  4. IPreferencesService.SetScanMode("Camera") is called
  5. IPreferencesService.SetDefaultScanType("ENTRY") is called
  6. IPreferencesService.SetSetupCompleted(true) is called
  7. Navigation proceeds to MainPage

### AC6: Setup.Completed flag controls navigation
- **Given** Setup.Completed is set to true in Preferences (device has been configured previously)
- **When** the application is launched
- **Then** Shell navigation routes directly to MainPage, bypassing SetupPage entirely

### AC7: Save failure shows clear error message
- **Given** IT Admin Ian presses Save with valid inputs
- **When** the save operation fails (e.g., SecureStorage unavailable, ISecureConfigService throws SecureStorageUnavailableException)
- **Then** an error banner is displayed at the top of the form with the message "Failed to save configuration: {error description}. Please try again.", the Setup.Completed flag is NOT set to true, and the user remains on SetupPage with their entered values preserved

### AC8: MVVM architecture with SetupViewModel
- **Given** SetupPage is implemented
- **When** the code is reviewed
- **Then** SetupPage.xaml binds to SetupViewModel, all business logic (validation, save, navigation) is in SetupViewModel, the ViewModel uses [ObservableProperty] and [RelayCommand] attributes from CommunityToolkit.Mvvm, and the code-behind (SetupPage.xaml.cs) contains no business logic

---

## Scope

### In Scope
- SetupPage.xaml with form layout (vertical stack of labeled fields)
- SetupViewModel.cs with ObservableProperty fields and RelayCommand for save
- Input validation (URL format, required fields) with inline error messages
- Credential storage via ISecureConfigService (API key, HMAC secret)
- Settings storage via IPreferencesService (server URL, scan mode, scan type)
- Setup.Completed flag management
- Shell navigation guard in AppShell.xaml.cs (conditional startup route)
- DI registration of SetupViewModel
- Page header with app name "SmartLog Scanner" and subtitle "Device Setup"
- Unit tests for SetupViewModel validation and save logic using xUnit + Moq

### Out of Scope
- "Test Connection" button (covered by US0005)
- Reconfiguration / settings page after initial setup
- Branding, logo, or splash screen during setup
- Setup wizard multi-step/multi-page flow (single page with all fields)
- Password visibility toggle (show/hide) for API key and HMAC secret fields
- Server URL auto-discovery or scanning

---

## Technical Notes

### Implementation Details

**Shell navigation guard pattern:**
```csharp
// AppShell.xaml.cs
protected override async void OnNavigated(ShellNavigatedEventArgs args)
{
    base.OnNavigated(args);

    var preferencesService = Handler.MauiContext.Services.GetRequiredService<IPreferencesService>();
    if (!preferencesService.GetSetupCompleted())
    {
        await Shell.Current.GoToAsync("//setup");
    }
}
```

**SetupViewModel key properties:**
```csharp
[ObservableProperty] private string _serverUrl = string.Empty;
[ObservableProperty] private string _apiKey = string.Empty;
[ObservableProperty] private string _hmacSecret = string.Empty;
[ObservableProperty] private string _selectedScanMode = "USB";
[ObservableProperty] private string _selectedScanType = "ENTRY";
[ObservableProperty] private string? _serverUrlError;
[ObservableProperty] private string? _apiKeyError;
[ObservableProperty] private string? _hmacSecretError;
[ObservableProperty] private string? _saveError;
[ObservableProperty] private bool _isSaving;
```

**Route registration in AppShell.xaml:**
```xml
<ShellContent Route="setup" ContentTemplate="{DataTemplate views:SetupPage}" />
<ShellContent Route="main" ContentTemplate="{DataTemplate views:MainPage}" />
```

**Validation helper:** Use a private ValidateAll() method that sets error properties and returns a bool. Called by SaveCommand.CanExecute and SaveCommand.Execute.

### API Contracts
Not applicable (no HTTP calls in this story; connection testing is US0005).

### Data Requirements

**Fields mapped to storage:**

| Field | Storage Service | Storage Key | Type |
|-------|----------------|-------------|------|
| Server URL | IPreferencesService | Server.BaseUrl | string |
| API Key | ISecureConfigService | Server.ApiKey | string (encrypted) |
| HMAC Secret | ISecureConfigService | Security.HmacSecretKey | string (encrypted) |
| Scan Mode | IPreferencesService | Scanner.Mode | string ("Camera" or "USB") |
| Default Scan Type | IPreferencesService | Scanner.DefaultScanType | string ("ENTRY" or "EXIT") |
| Setup Completed | IPreferencesService | Setup.Completed | bool |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| All fields empty and Save pressed | Inline error "This field is required" shown below Server URL, API Key, and HMAC Secret fields; save not executed; Scan Mode and Scan Type have defaults so no error |
| Malformed URL: "not-a-url" | Inline error "Please enter a valid URL (e.g., https://192.168.1.100:8443)" below Server URL field |
| URL without scheme: "192.168.1.100:8443" | Inline error displayed; URL must include http:// or https:// scheme |
| URL with trailing slash: "https://server.local:8443/" | Accepted as valid; trailing slash preserved as entered |
| Special characters in API key: "sk-abc+/=!@#$%^&*()" | Stored correctly via SecureStorage; no character restrictions on API key |
| Very long API key (> 1000 characters) | Accepted and stored; no artificial length limits enforced by the setup form |
| Back navigation during setup (hardware back button or swipe) | Navigation is blocked; user cannot leave SetupPage without completing setup; Shell navigation guard re-routes to SetupPage |
| App killed during setup (force quit before Save completes) | On re-launch, Setup.Completed is still false (save is atomic -- flag set last); SetupPage shown again; user re-enters values |
| Re-launch after partial setup (SecureStorage wrote API key but crashed before writing HMAC secret) | Setup.Completed is false (set last in the save sequence); SetupPage shown; user re-enters all values; previous partial values overwritten |
| SecureStorage unavailable when Save is pressed | Error banner: "Failed to save configuration: Secure storage is unavailable on this device. Please check system keychain settings." User can retry |
| Picker defaults: user does not change Scan Mode or Scan Type | Default values "USB" and "ENTRY" are used and stored correctly |
| Server URL with IP address and port: "https://10.0.0.5:8443" | Accepted as valid URL |
| Server URL with hostname: "https://smartlog.school.local" | Accepted as valid URL |

---

## Test Scenarios

- [ ] SetupPage is displayed when Setup.Completed is false on app launch
- [ ] MainPage is displayed when Setup.Completed is true on app launch
- [ ] Server URL field accepts valid HTTPS URL and clears error
- [ ] Server URL field accepts valid HTTP URL and clears error
- [ ] Server URL field shows error for empty value on save
- [ ] Server URL field shows error for malformed URL (e.g., "not-a-url")
- [ ] Server URL field shows error for URL missing scheme (e.g., "192.168.1.100")
- [ ] API Key field shows error for empty value on save
- [ ] HMAC Secret field shows error for empty value on save
- [ ] API Key and HMAC Secret fields are masked (password input type)
- [ ] Scan Mode picker contains exactly two options: "Camera" and "USB"
- [ ] Scan Mode picker defaults to "USB"
- [ ] Default Scan Type picker contains exactly two options: "ENTRY" and "EXIT"
- [ ] Default Scan Type picker defaults to "ENTRY"
- [ ] Successful save calls ISecureConfigService.SetApiKeyAsync with entered API key value
- [ ] Successful save calls ISecureConfigService.SetHmacSecretAsync with entered HMAC secret value
- [ ] Successful save calls IPreferencesService.SetServerBaseUrl with entered URL value
- [ ] Successful save calls IPreferencesService.SetScanMode with selected scan mode
- [ ] Successful save calls IPreferencesService.SetDefaultScanType with selected scan type
- [ ] Successful save sets Setup.Completed to true via IPreferencesService
- [ ] Successful save navigates to MainPage
- [ ] Save failure displays error banner with descriptive message
- [ ] Save failure does NOT set Setup.Completed to true
- [ ] Save failure preserves all entered field values
- [ ] Save button is disabled while save operation is in progress (IsSaving = true)
- [ ] Multiple rapid Save button taps only trigger one save operation
- [ ] Validation errors clear when user corrects the invalid field
- [ ] SetupViewModel uses [ObservableProperty] and [RelayCommand] from CommunityToolkit.Mvvm
- [ ] SetupPage.xaml.cs code-behind contains no business logic

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0001 | Hard | ISecureConfigService and IPreferencesService interfaces and implementations for credential and preference storage | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| CommunityToolkit.Mvvm | NuGet Package | Available (source generators for ObservableProperty, RelayCommand) |
| .NET MAUI Shell Navigation | Platform SDK | Available in .NET 8.0 MAUI |
| .NET MAUI XAML (Entry, Picker, Button, Label controls) | Platform SDK | Available in .NET 8.0 MAUI |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

---

## Open Questions

- [ ] Should the setup wizard support a "Reset" or "Re-configure" flow accessible from a future settings page, or is setup strictly one-time for v1.0? - Owner: Product
- [ ] Should password fields (API Key, HMAC Secret) include a "show/hide" toggle for usability during entry? - Owner: Product/UX
- [ ] Should the server URL field strip trailing slashes before storing, or preserve the URL exactly as entered? - Owner: Architect
- [ ] What is the minimum form layout width to support on smaller laptop screens? - Owner: UX

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
