# US0128: Remove AboutPage and Surface Author Metadata in OS App Properties

> **Status:** Draft
> **Epic:** [EP0018: Scanner Slim-down](../epics/EP0018-scanner-slim-down.md)
> **Owner:** AI Assistant
> **Reviewer:** Mark Daniel Marmeto
> **Created:** 2026-05-06

## User Story

**As** Mark Daniel Marmeto (the developer/maintainer)
**I want** the dead `AboutPage` removed from the Shell menu, and my author / company / copyright credit baked into the built artifacts so it shows in Windows file properties and macOS Get Info
**So that** the Shell menu only contains operationally-useful pages, and ownership of the binary is verifiable at the OS level without an in-app menu entry

## Context

### Background

`AboutPage.xaml` (155 lines) is a static info card with logo, app name, hardcoded `Version 1.0.0`, description, author credit ("Mark Daniel Marmeto"), tech-stack chips, and copyright. It is registered as a `<ShellContent>` route in `AppShell.xaml`, giving it a menu entry that operators see at every gate.

Two problems:

1. **Dead surface**: this is a kiosk-style scanner app where operators shouldn't be navigating menus. The hardcoded `Version 1.0.0` proves nobody is maintaining the page — it's been frozen since release while the actual app version has moved on.
2. **Author credit lives only inside the app**: the `.exe` and `.app` produced by the build have **no** Company / Authors / Copyright metadata set in `SmartLog.Scanner.csproj`. So right-clicking the binary on Windows (Properties → Details) or running Get Info on macOS shows blank fields. The author credit is unreachable without launching the app.

This story moves the credit from a runtime UI surface to OS-level binary metadata, where it belongs, and removes the dead page.

### Truthful note on platform fields

Neither Windows nor macOS exposes a literal "Author" property on application bundles. The reachable fields are:

- **Windows** (Properties → Details): `Company`, `Product name`, `Copyright`, `File description`. Set via csproj `<Company>`, `<Product>`, `<Copyright>`.
- **macOS** (Get Info on `.app`): `Copyright`, `Version`. Set via csproj `<Copyright>` (flows to `NSHumanReadableCopyright` in Info.plist) and `<ApplicationDisplayVersion>` (already set).
- `<Authors>` is a NuGet/SDK field that gets baked into assembly metadata; surfaces in some inspector tools but not the standard Properties / Get Info dialogs.

So this story sets `<Authors>`, `<Company>`, `<Copyright>`, `<Product>` to ensure both reachable and inspector-visible fields carry the credit. The verification ACs check the actually-visible fields, not a fictional "Author" field.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| TRD | Platform | Cross-platform (macOS MacCatalyst + Windows) | Verification must occur on both `net8.0-maccatalyst` and `net8.0-windows10.0.19041.0` outputs |
| EP0018 | Scope | No UX redesign | Only the Shell menu loses an entry; no other page is touched |

---

## Acceptance Criteria

### AC1: AboutPage files are removed

- **Given** the codebase before this story
- **When** the story is complete
- **Then** `SmartLog.Scanner/Views/AboutPage.xaml` and `SmartLog.Scanner/Views/AboutPage.xaml.cs` no longer exist, and no other source file references `AboutPage`

### AC2: Shell route is removed

- **Given** `AppShell.xaml` previously had a `<ShellContent ContentTemplate="{DataTemplate local:AboutPage}" />` entry
- **When** the story is complete
- **Then** that `ShellContent` is removed and no Shell route resolution error appears in the logs at app launch on either platform

### AC3: Windows binary shows Company and Copyright

- **Given** a release build is produced via `dotnet publish SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release`
- **When** the published `SmartLog.Scanner.exe` is right-clicked → Properties → Details
- **Then** the **Company** field shows `Mark Daniel Marmeto` and the **Copyright** field contains `Mark Daniel Marmeto` (e.g., `© 2026 Mark Daniel Marmeto`)

### AC4: macOS bundle shows Copyright in Get Info

- **Given** a release build is produced for `net8.0-maccatalyst`
- **When** the produced `SmartLog.Scanner.app` is selected in Finder and Get Info is opened (`⌘I`)
- **Then** the **Copyright** line shows `© 2026 Mark Daniel Marmeto`

### AC5: csproj metadata properties exist

- **Given** the `SmartLog.Scanner.csproj` file
- **When** opened
- **Then** the first `<PropertyGroup>` (or a clearly-named one) contains:
  - `<Authors>Mark Daniel Marmeto</Authors>`
  - `<Company>Mark Daniel Marmeto</Company>`
  - `<Copyright>© 2026 Mark Daniel Marmeto</Copyright>`
  - `<Product>SmartLog Scanner</Product>`

### AC6: App still builds and launches on both platforms

- **Given** the changes from AC1–AC5
- **When** `dotnet build SmartLog.Scanner -f net8.0-maccatalyst` and `dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0` are run
- **Then** both builds succeed with no new warnings, and the app launches to `MainPage` (or `SetupPage` on first run) without errors

---

## Scope

### In Scope
- Delete `AboutPage.xaml` and `AboutPage.xaml.cs`
- Remove the `ShellContent` route from `AppShell.xaml`
- Remove any other references to `AboutPage` (navigation calls, Shell `Routing.RegisterRoute`, etc.) — to be discovered via grep during the plan
- Add `<Authors>`, `<Company>`, `<Copyright>`, `<Product>` to `SmartLog.Scanner.csproj`
- If `<Copyright>` does not flow into the macCatalyst Info.plist `NSHumanReadableCopyright` automatically, add it explicitly in `Platforms/MacCatalyst/Info.plist`

### Out of Scope
- Surfacing the version (or any "About" content) elsewhere in the app — operators don't need it; OS-level metadata is sufficient
- Updating the README or any documentation that mentions the About page (a sweep can be a follow-up if desired)
- Changing `ApplicationDisplayVersion` or `ApplicationVersion`
- Code-signing changes (separate concern)
- Author credits in WebApp (different project)

---

## Technical Notes

### Files to change

| File | Change |
|------|--------|
| `SmartLog.Scanner/Views/AboutPage.xaml` | Delete |
| `SmartLog.Scanner/Views/AboutPage.xaml.cs` | Delete |
| `SmartLog.Scanner/AppShell.xaml` | Remove the `<ShellContent>` referencing `AboutPage` |
| `SmartLog.Scanner/SmartLog.Scanner.csproj` | Add `<Authors>`, `<Company>`, `<Copyright>`, `<Product>` properties |
| `SmartLog.Scanner/Platforms/MacCatalyst/Info.plist` | Add `<key>NSHumanReadableCopyright</key><string>© 2026 Mark Daniel Marmeto</string>` *if csproj `<Copyright>` does not propagate (verify during plan)* |

### csproj property snippet

```xml
<PropertyGroup>
    <Authors>Mark Daniel Marmeto</Authors>
    <Company>Mark Daniel Marmeto</Company>
    <Copyright>© 2026 Mark Daniel Marmeto</Copyright>
    <Product>SmartLog Scanner</Product>
</PropertyGroup>
```

### Verification commands

```bash
# macOS — inspect built .app
plutil -p ./SmartLog.Scanner/bin/Release/net8.0-maccatalyst/.../SmartLog.Scanner.app/Contents/Info.plist | grep -i copyright

# Windows — inspect built .exe (run on Windows)
powershell -Command "(Get-Item .\SmartLog.Scanner.exe).VersionInfo | Format-List CompanyName, ProductName, LegalCopyright"
```

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|--------------------|
| Another file imports `SmartLog.Scanner.Views.AboutPage` | Build fails fast — caught during AC6 build step |
| `Routing.RegisterRoute("//AboutPage", ...)` exists somewhere | Must also be removed; surfaces during grep in the plan |
| User has the app pinned to Windows taskbar with About in jump list | No-op — Shell menu is in-app only, not OS-level jump list |
| Localization files reference "About" page title | None present today; if any exist, remove the keys |
| `<Copyright>` doesn't populate macOS Info.plist | Fallback: add `NSHumanReadableCopyright` explicitly in `Platforms/MacCatalyst/Info.plist` |

---

## Test Scenarios

- [ ] `find SmartLog.Scanner -name "AboutPage*"` returns no results after the story
- [ ] `grep -r "AboutPage" SmartLog.Scanner SmartLog.Scanner.Core` returns no results
- [ ] `dotnet build SmartLog.Scanner -f net8.0-maccatalyst` succeeds with no new warnings
- [ ] `dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0` succeeds with no new warnings
- [ ] App launches and reaches `MainPage` (or `SetupPage` on first run) on both platforms
- [ ] Shell menu (hamburger) shows only Main / ScanLogs / OfflineQueue — three entries
- [ ] Windows: built `.exe` Properties → Details shows Company = `Mark Daniel Marmeto`
- [ ] Windows: built `.exe` Properties → Details shows Copyright containing `Mark Daniel Marmeto`
- [ ] macOS: built `.app` Get Info shows Copyright containing `Mark Daniel Marmeto`
- [ ] Existing test suite still passes (`dotnet test SmartLog.Scanner.Tests`)

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| None | — | — | — |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| MAUI MSBuild SDK assembly metadata flow | Build tooling | Available |
| Access to a Windows machine (or Windows VM) for AC3 verification | Verification | Required |
| Access to a macOS machine for AC4 verification | Verification | Required (developer's primary) |

---

## Estimation

**Story Points:** 1
**Complexity:** Low — file deletions plus 4 csproj properties; only friction is verifying the OS-level fields populate on both platforms (especially confirming whether `<Copyright>` flows to Info.plist or needs an explicit `NSHumanReadableCopyright`).

---

## Open Questions

- [ ] Does MAUI's `net8.0-maccatalyst` target propagate `<Copyright>` from csproj into the bundle's `Info.plist` `NSHumanReadableCopyright`, or is an explicit Info.plist edit required? Resolve during plan by inspecting a current debug-build's Info.plist.

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-05-06 | AI Assistant | Initial draft from Scanner Slim-down review (EP0018). |
