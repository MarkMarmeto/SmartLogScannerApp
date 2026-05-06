# PL0026: Remove AboutPage and Surface Author Metadata in OS App Properties — Implementation Plan

> **Status:** Draft
> **Story:** [US0128: Remove AboutPage and Surface Author Metadata in OS App Properties](../stories/US0128-remove-aboutpage-and-author-metadata.md)
> **Epic:** [EP0018: Scanner Slim-down](../epics/EP0018-scanner-slim-down.md)
> **Created:** 2026-05-06
> **Language:** C# / MAUI XAML / MSBuild

---

## Overview

Two-part cleanup: (1) remove the dead `AboutPage` Shell entry and its files; (2) bake author / company / copyright metadata into the built `.exe` and `.app` so OS-level file properties surface the credit. No new runtime code paths; no UX rewrite.

The csproj-properties part is straightforward MSBuild. The platform-uncertainty is whether MAUI's `<Copyright>` and `<Authors>` flow into the macCatalyst bundle's `Info.plist` automatically. The plan resolves this with a discovery step before final commits — if propagation is missing, an explicit `NSHumanReadableCopyright` entry is added to `Platforms/MacCatalyst/Info.plist`.

---

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | AboutPage files removed | `AboutPage.xaml` + `AboutPage.xaml.cs` deleted; no source references remain |
| AC2 | Shell route removed | `<ShellContent>` referencing `AboutPage` removed from `AppShell.xaml` |
| AC3 | Windows binary metadata | `.exe` Properties → Details shows `Mark Daniel Marmeto` Company + Copyright |
| AC4 | macOS bundle metadata | `.app` Get Info shows `Mark Daniel Marmeto` Copyright |
| AC5 | csproj properties present | `<Authors>`, `<Company>`, `<Copyright>`, `<Product>` set |
| AC6 | App still builds + launches | Both TFMs build clean; app starts to MainPage/SetupPage |

---

## Technical Context

### Language & Framework
- **csproj:** MSBuild SDK-style PropertyGroup
- **XAML:** MAUI Shell (`<ShellContent>`)
- **Test Framework:** Manual verification on each platform (no unit tests for OS metadata)

### Existing Patterns
- `AppShell.xaml` lists Shell routes as siblings under the Shell root — order matters for Windows because the platform handler renders the first `ShellContent` initially (per `AppShell.xaml` lines 13–18 comment)
- csproj currently has `<ApplicationTitle>`, `<ApplicationId>`, `<ApplicationDisplayVersion>`, `<ApplicationVersion>` set in the main `<PropertyGroup>` — new metadata properties slot into the same group
- `Platforms/MacCatalyst/Info.plist` exists and is owned by the build (verify whether MAUI tooling regenerates or merges)

### Relevant Best Practices
- MSBuild SDK-style projects auto-generate assembly-level attributes (`AssemblyCompany`, `AssemblyCopyright`, `AssemblyProduct`) from PropertyGroup values when `<GenerateAssemblyInfo>` is true (default) — Windows reads these for File Properties
- macCatalyst Info.plist has its own keys (`NSHumanReadableCopyright`, `CFBundleName`); MAUI's csproj-to-plist propagation varies by template version; verify before assuming
- Use the encoded `&#169;` or literal `©` in csproj `<Copyright>` consistently with the assembly attribute

---

## Recommended Approach

**Strategy:** Manual-verified
**Rationale:** OS-level binary metadata is not unit-testable from the test project (which targets `net8.0`, not a MAUI TFM). Verification is by inspection of built artifacts on each platform.

### Verification Priority
1. macCatalyst `.app` Info.plist `NSHumanReadableCopyright` after build (decides whether explicit Info.plist edit is needed)
2. Windows `.exe` File Properties → Details → Company / Copyright fields
3. App launch + Shell navigation works on both platforms

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Find all `AboutPage` references via grep | (n/a — discovery) | — | [ ] |
| 2 | Delete `AboutPage.xaml` and `AboutPage.xaml.cs` | `Scanner/Views/AboutPage.xaml*` | 1 | [ ] |
| 3 | Remove `<ShellContent>` for AboutPage in AppShell | `Scanner/AppShell.xaml` | 1 | [ ] |
| 4 | Remove any other references found by grep (route registrations, navigation calls) | various | 1 | [ ] |
| 5 | Add `<Authors>`, `<Company>`, `<Copyright>`, `<Product>` to csproj | `Scanner/SmartLog.Scanner.csproj` | — | [ ] |
| 6 | Build both TFMs locally; check Mac Info.plist for `NSHumanReadableCopyright` | (n/a — discovery) | 5 | [ ] |
| 7 | If macOS plist missing copyright, add explicit `NSHumanReadableCopyright` | `Platforms/MacCatalyst/Info.plist` | 6 | [ ] |
| 8 | Manual verify Windows binary properties (right-click → Details) | (n/a — verification) | 5 | [ ] |
| 9 | Manual verify macOS bundle Get Info | (n/a — verification) | 5–7 | [ ] |
| 10 | Run test suite to confirm no breakage | `Scanner.Tests` | 2–4 | [ ] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| A | 1 (discovery) | — |
| B | 2, 3, 4 | A |
| C | 5 | — (parallel to B) |
| D | 6 (Mac plist check) | C |
| E | 7 (Mac plist edit, conditional) | D |
| F | 8, 9, 10 (verification) | B, C, E |

---

## Implementation Phases

### Phase 1: Discovery — find all AboutPage references

**Goal:** Avoid leaving dangling references that would break the build.

```bash
grep -rn "AboutPage" \
  /Users/markmarmeto/Projects/SmartLogScannerApp/SmartLog.Scanner \
  /Users/markmarmeto/Projects/SmartLogScannerApp/SmartLog.Scanner.Core \
  /Users/markmarmeto/Projects/SmartLogScannerApp/SmartLog.Scanner.Tests \
  --include="*.cs" --include="*.xaml"
```

Expected hits (delete-or-edit):
- `Views/AboutPage.xaml` (delete)
- `Views/AboutPage.xaml.cs` (delete)
- `AppShell.xaml` line ~37 — `<ShellContent ContentTemplate="{DataTemplate local:AboutPage}" />` (delete the block)
- Any `Routing.RegisterRoute(...AboutPage...)` calls (delete)
- Any `Shell.Current.GoToAsync("//AboutPage")` (delete or redirect — none expected)

If the grep finds something unexpected (e.g., a test that imports AboutPage), pause and surface it before continuing.

---

### Phase 2: File deletions

**Goal:** Remove the page and its Shell route.

- [ ] Delete `SmartLog.Scanner/Views/AboutPage.xaml`
- [ ] Delete `SmartLog.Scanner/Views/AboutPage.xaml.cs`
- [ ] Edit `SmartLog.Scanner/AppShell.xaml`: remove the `<ShellContent>` block referencing `AboutPage`. Keep the surrounding Shell structure unchanged.

**Files:**
- `SmartLog.Scanner/Views/AboutPage.xaml` (delete)
- `SmartLog.Scanner/Views/AboutPage.xaml.cs` (delete)
- `SmartLog.Scanner/AppShell.xaml` (edit)

---

### Phase 3: Add csproj metadata

**Goal:** Author / company / copyright / product properties baked into both built artifacts.

**File:** `SmartLog.Scanner/SmartLog.Scanner.csproj`

Add inside the existing main `<PropertyGroup>` (the one containing `<ApplicationTitle>` and `<ApplicationId>`):

```xml
<Authors>Mark Daniel Marmeto</Authors>
<Company>Mark Daniel Marmeto</Company>
<Copyright>© 2026 Mark Daniel Marmeto</Copyright>
<Product>SmartLog Scanner</Product>
```

Notes:
- Use the literal `©` character. MSBuild handles UTF-8 encoded source files; the csproj is UTF-8.
- Do **not** set `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` — we rely on the default (true) so the SDK auto-generates `[assembly: AssemblyCompany("Mark Daniel Marmeto")]` etc., which Windows reads for File Properties.

---

### Phase 4: macOS Info.plist propagation check

**Goal:** Confirm whether MAUI emits `NSHumanReadableCopyright` from `<Copyright>`. If not, add it explicitly.

#### 4a — Inspect built .app Info.plist after Phase 3

```bash
# Build for macCatalyst
dotnet build SmartLog.Scanner -f net8.0-maccatalyst -c Debug

# Inspect the resulting Info.plist (path varies — adapt to actual output)
plutil -p SmartLog.Scanner/bin/Debug/net8.0-maccatalyst/SmartLog.Scanner.app/Contents/Info.plist | grep -iE 'copyright|humanreadable'
```

#### 4b — Decision

- **If** `NSHumanReadableCopyright` is present and contains `Mark Daniel Marmeto` → no further plist edit needed; proceed to Phase 5.
- **If** absent or empty → add explicit entry to `SmartLog.Scanner/Platforms/MacCatalyst/Info.plist`:

```xml
<key>NSHumanReadableCopyright</key>
<string>© 2026 Mark Daniel Marmeto</string>
```

The MAUI build merges this into the final bundle plist.

---

### Phase 5: Verification

**Goal:** Both platforms surface the metadata correctly; app launches.

#### 5a — Build both TFMs

```bash
dotnet build SmartLog.Scanner -f net8.0-maccatalyst -c Release
dotnet build SmartLog.Scanner -f net8.0-windows10.0.19041.0 -c Release
```

Both must succeed with no new warnings.

#### 5b — macOS Get Info

1. Locate the built `.app` in `bin/Release/net8.0-maccatalyst/`
2. Select in Finder, press `⌘I`
3. Confirm the **Copyright** line shows `© 2026 Mark Daniel Marmeto`

#### 5c — Windows File Properties

1. Copy the published `.exe` from the build to a Windows machine (or VM)
2. Right-click → Properties → Details
3. Confirm:
   - **Company** = `Mark Daniel Marmeto`
   - **Copyright** contains `Mark Daniel Marmeto`
   - **Product name** = `SmartLog Scanner`

#### 5d — App launch

Run the app on each platform and confirm:
- Reaches `MainPage` (or `SetupPage` on first run) without errors
- Shell hamburger menu shows three entries (MainPage, ScanLogsPage, OfflineQueuePage) — no AboutPage entry
- No Shell route resolution warnings in `Serilog` logs

#### 5e — Tests still pass

```bash
dotnet test SmartLog.Scanner.Tests -c Release
```

All tests pass — no test should reference AboutPage.

---

## Edge Case Handling Plan

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | Another file imports `AboutPage` | Caught by grep (Phase 1) before deletes; if found in unexpected place, pause and ask | Phase 1 |
| 2 | `Routing.RegisterRoute` for AboutPage | Caught by grep (Phase 1); deleted in Phase 2 | Phase 1–2 |
| 3 | Localization strings reference About page title | None found in current codebase (verify via grep for "About" within Resources/) | Phase 1 |
| 4 | `<Copyright>` doesn't propagate to Mac plist | Phase 4 detects; Phase 4b adds explicit `NSHumanReadableCopyright` | Phase 4 |
| 5 | Windows pinned-app jump list shows About | Out of scope — Shell menu is in-app; OS jump list is configured separately and not currently set | n/a |

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Shell first-rendered page changes when AboutPage is removed | UX regression — Windows would render different first page | Verify the Shell ordering: per AppShell.xaml comment lines 13–18, MainPage is the first ShellContent. AboutPage appears later in the XAML. Removing it does not change the first-rendered page |
| Missing csproj `<GenerateAssemblyInfo>` causes duplicate assembly attribute error | Build failure | Default is `true` and current csproj does not override; if a user's project has overridden it, Phase 3 adjustment surfaces the error; resolve by removing the override |
| Test project (`Scanner.Tests`) references AboutPage | Build failure | Phase 1 grep includes `Scanner.Tests`; remove any references found |
| `©` character mojibake in csproj if file encoding drift | Wrong copyright string in binary | Verify csproj is UTF-8 (no BOM is fine for csproj); MSBuild + dotnet read UTF-8 by default |
| Mac plist explicit edit gets overwritten by csproj propagation later | Duplicate or conflicting `NSHumanReadableCopyright` | If Phase 4 finds csproj already populating the plist, **skip 4b** — don't add the explicit entry |

---

## Definition of Done

- [ ] All 6 ACs implemented and verified
- [ ] `find SmartLog.Scanner SmartLog.Scanner.Core SmartLog.Scanner.Tests -name "AboutPage*"` returns no results
- [ ] `grep -rn "AboutPage" SmartLog.Scanner SmartLog.Scanner.Core SmartLog.Scanner.Tests --include="*.cs" --include="*.xaml"` returns no results
- [ ] csproj contains all four metadata properties
- [ ] macOS Get Info on built `.app` shows the correct Copyright string
- [ ] Windows File Properties → Details on built `.exe` shows the correct Company + Copyright + Product Name
- [ ] App launches on both TFMs and reaches MainPage/SetupPage
- [ ] `dotnet test SmartLog.Scanner.Tests` passes

---

## Notes

- The hardcoded `Version 1.0.0` in `AboutPage.xaml` (a noted bug in the story Context) is moot once the page is deleted. No follow-up story needed.
- This plan does **not** add a version label to MainPage. If you want one later, that's a separate story.
- The `<Authors>` field is primarily a NuGet metadata field; for an app project (not a NuGet package) it is informational. We set it for completeness; AC verification focuses on the OS-visible Company and Copyright fields.
- The `Open Question` from the story (whether `<Copyright>` propagates to macOS plist) is resolved at Phase 4 by inspection rather than guessed up front.
