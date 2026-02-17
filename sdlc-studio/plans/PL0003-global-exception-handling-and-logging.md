# PL0003: Global Exception Handling and Logging - Implementation Plan

> **Status:** Completed
> **Story:** [US0003: Implement Global Exception Handling and Logging](../stories/US0003-global-exception-handling-and-logging.md)
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Created:** 2026-02-14
> **Language:** C#

## Overview

This plan completes the global exception handling and logging infrastructure for SmartLog Scanner. **Serilog is already partially configured** in MauiProgram.cs (file + console sinks, rolling daily files, 31-day retention). This plan adds:
1. Global exception handlers (AppDomain.UnhandledException, TaskScheduler.UnobservedTaskException)
2. Configurable minimum log level from appsettings.json
3. File size limit (100 MB with rollover)
4. SourceContext enrichment for better log organization
5. Log directory auto-creation

The implementation uses **Test-After** approach because:
1. Exception handlers are event-driven infrastructure (hard to unit test)
2. Serilog configuration is straightforward (already working)
3. Manual verification more appropriate (trigger exceptions, check logs)

## Acceptance Criteria Summary

| AC | Name | Description |
|----|------|-------------|
| AC1 | File sink configured | Logs to {AppDataDirectory}/logs/smartlog-scanner-.log with date rolling |
| AC2 | Console sink configured | Logs to stdout with structured format |
| AC3 | Rolling file policy | Daily rollover, 31-day retention |
| AC4 | AppDomain.UnhandledException | Captured, logged at Fatal level, graceful shutdown |
| AC5 | TaskScheduler.UnobservedTaskException | Captured, logged at Error level, marked observed, app continues |
| AC6 | Configurable log level | MinimumLevel from appsettings.json Logging:MinimumLevel |
| AC7 | Default log level Information | When key absent, defaults to Information |
| AC8 | Graceful recovery | App state preserved, restarts normally |

---

## Technical Context

### Language & Framework
- **Primary Language:** C# 12
- **Framework:** .NET 8.0 MAUI (MacCatalyst + Windows)
- **Test Framework:** Manual verification (log file inspection, exception triggering)

### Relevant Best Practices
- Exception handlers registered early in app lifecycle (before DI, navigation)
- Log.CloseAndFlush() on app shutdown to ensure buffered entries written
- SetObserved() on TaskScheduler exceptions to prevent process termination
- FileSystem.AppDataDirectory for cross-platform log location
- Serilog self-log to console for diagnostic issues

### Library Documentation (Context7)

| Library | Key Patterns |
|---------|--------------|
| Serilog | `Log.Logger = new LoggerConfiguration()...CreateLogger()`, `Log.Fatal/Error/Warning/Information/Debug` |
| Serilog.Sinks.File | `WriteTo.File(path, rollingInterval, retainedFileCountLimit, fileSizeLimitBytes)` |
| Serilog.Sinks.Console | `WriteTo.Console(outputTemplate)` |
| Serilog.Extensions.Logging | `builder.Logging.AddSerilog(Log.Logger)` integrates with ILogger<T> |

### Existing Patterns
- **MauiProgram.cs**: Serilog already configured (lines 24-33), but missing:
  - Configurable minimum level from appsettings.json
  - File size limit
  - SourceContext enrichment
  - Global exception handlers

---

## Recommended Approach

**Strategy:** Test-After (Code First)
**Rationale:**
- Exception handlers are infrastructure code (hard to unit test without triggering real exceptions)
- Serilog configuration is straightforward
- Manual verification via log file inspection and exception triggering is most practical

### Test Priority
1. **Manual test**: Trigger unhandled exception, verify Fatal log entry created
2. **Manual test**: Trigger unobserved task exception, verify Error log entry, app continues
3. **Manual test**: Change Logging:MinimumLevel to Debug, verify Debug entries appear
4. **Manual test**: Check log file rollover after midnight (or simulate with date change)

---

## Implementation Tasks

| # | Task | File | Depends On | Status |
|---|------|------|------------|--------|
| 1 | Add Logging:MinimumLevel to appsettings.json | `Resources/Raw/appsettings.json` | None | [ ] |
| 2 | Update Serilog config: load min level from config | `MauiProgram.cs` | Task 1 | [ ] |
| 3 | Update Serilog config: add file size limit | `MauiProgram.cs` | None | [ ] |
| 4 | Update Serilog config: add SourceContext enrichment | `MauiProgram.cs` | None | [ ] |
| 5 | Create log directory if not exists | `MauiProgram.cs` | None | [ ] |
| 6 | Register AppDomain.UnhandledException handler | `MauiProgram.cs` | None | [ ] |
| 7 | Register TaskScheduler.UnobservedTaskException handler | `MauiProgram.cs` | None | [ ] |
| 8 | Add Log.CloseAndFlush() on app shutdown | `App.xaml.cs` or platform-specific | Task 6 | [ ] |

### Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|--------------|
| Group 1 | Task 1 | None |
| Group 2 | Tasks 2-7 | Task 1 complete (all in MauiProgram.cs, sequential) |
| Group 3 | Task 8 | Group 2 complete |

---

## Implementation Phases

### Phase 1: Update appsettings.json (Task 1)
**Goal:** Add Logging:MinimumLevel configuration key

- [ ] Add to `Resources/Raw/appsettings.json`:
  ```json
  {
    "Logging": {
      "MinimumLevel": "Information"
    }
  }
  ```
  (Already exists from project setup, verify value)

**Files:**
- `SmartLog.Scanner/Resources/Raw/appsettings.json` - Add/verify Logging section

### Phase 2: Update Serilog Configuration (Tasks 2-5)
**Goal:** Complete Serilog configuration with all AC requirements

Current configuration (lines 24-33):
```csharp
var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs", "smartlog-scanner-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(...)
    .WriteTo.Console()
    .CreateLogger();
```

**Update to:**
```csharp
// AC1/AC3: Create log directory if not exists
var logDir = Path.Combine(FileSystem.AppDataDirectory, "logs");
Directory.CreateDirectory(logDir); // No-op if exists

var logPath = Path.Combine(logDir, "smartlog-scanner-.log");

// AC6/AC7: Load minimum level from config (default: Information)
var minLevel = config.GetValue<string>("Logging:MinimumLevel", "Information");
var logLevel = Enum.Parse<LogEventLevel>(minLevel, ignoreCase: true);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(logLevel)
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31,
        fileSizeLimitBytes: 100_000_000, // AC3: 100 MB limit
        rollOnFileSizeLimit: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Console(
        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext() // AC4: SourceContext enrichment
    .CreateLogger();
```

**Files:**
- `MauiProgram.cs` - Update Serilog configuration (replace lines 24-33)

### Phase 3: Global Exception Handlers (Tasks 6-7)
**Goal:** Capture all unhandled and unobserved exceptions

- [ ] Add exception handlers immediately after Serilog configuration in `MauiProgram.cs`:
  ```csharp
  // AC4: Capture AppDomain.UnhandledException (unhandled exceptions on any thread)
  AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
  {
      var exception = args.ExceptionObject as Exception;
      Log.Fatal(exception, "Unhandled AppDomain exception. IsTerminating: {IsTerminating}",
          args.IsTerminating);
      Log.CloseAndFlush();
  };

  // AC5: Capture TaskScheduler.UnobservedTaskException (unobserved async exceptions)
  TaskScheduler.UnobservedTaskException += (sender, args) =>
  {
      Log.Error(args.Exception, "Unobserved task exception");
      args.SetObserved(); // Prevent process termination, app continues
  };
  ```

- [ ] Add to global usings (if needed):
  ```csharp
  using System.Threading.Tasks;
  using Serilog.Events;
  ```

**Files:**
- `MauiProgram.cs` - Add exception handlers after Serilog config
- `MauiProgram.cs` (top) - Add using directives

### Phase 4: App Shutdown Cleanup (Task 8)
**Goal:** Ensure all buffered log entries are flushed on app exit

- [ ] In `App.xaml.cs` or platform-specific lifecycle events, add:
  ```csharp
  protected override void CleanUp()
  {
      Log.CloseAndFlush();
      base.CleanUp();
  }
  ```

**Note:** MAUI app lifecycle varies by platform. For now, the AppDomain.UnhandledException handler already calls Log.CloseAndFlush(). Consider adding to app shutdown events in future if needed.

**Files:**
- `App.xaml.cs` - Add CleanUp override (or skip if AppDomain handler sufficient)

### Phase 5: Testing & Validation
**Goal:** Verify all acceptance criteria

| AC | Verification Method | File Evidence | Status |
|----|---------------------|---------------|--------|
| AC1 | Check log file exists at AppDataDirectory/logs/ | File system inspection | Pending |
| AC2 | Run app, check console output shows log entries | Console output visible | Pending |
| AC3 | Check file name pattern (smartlog-scanner-20260214.log) | File system inspection | Pending |
| AC4 | Trigger unhandled exception, verify Fatal log entry | Manual exception throw + log file check | Pending |
| AC5 | Create unobserved Task, verify Error log, app continues | Manual Task.Run without await + log check | Pending |
| AC6 | Set Logging:MinimumLevel=Debug, verify Debug entries appear | appsettings.json edit + log check | Pending |
| AC7 | Remove Logging:MinimumLevel, verify Information default | appsettings.json edit + log check | Pending |
| AC8 | Trigger crash, restart app, verify state intact | Manual crash + restart | Pending |

---

## Edge Case Handling

| # | Edge Case (from Story) | Handling Strategy | Phase |
|---|------------------------|-------------------|-------|
| 1 | Log directory not writable | Serilog self-log to console; file sink fails silently; app continues | Phase 2 (Task 5) |
| 2 | Disk full | File sink fails silently; console sink works; app continues | Phase 2 (inherent) |
| 3 | Concurrent logging from multiple threads | Serilog file sink is thread-safe (built-in) | Phase 2 (inherent) |
| 4 | Exception during exception handling | Wrap handlers in try/catch; log to console via self-log | Phase 3 (Task 6-7) |
| 5 | App restart after crash | Serilog appends to existing log file; no corruption | Phase 2 (inherent) |
| 6 | Rapid log writes (1000+ entries/second) | Serilog buffers writes; async file sink; no UI blocking | Phase 2 (inherent) |
| 7 | FileSystem.AppDataDirectory platform differences | MAUI handles cross-platform paths automatically | Phase 2 (Task 5) |
| 8 | Invalid MinimumLevel value in config | Enum.Parse throws; catch and default to Information | Phase 2 (Task 2) |
| 9 | Log file exceeds 100 MB in one day | Serilog rolls to _001.log suffix; keeps original | Phase 2 (Task 3) |

**Coverage:** 9/9 edge cases handled

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Exception handler registered too late | Early exceptions missed | Register immediately after Serilog init, before DI/navigation |
| Log.CloseAndFlush() not called on crash | Buffered entries lost | AppDomain handler calls CloseAndFlush before termination |
| Invalid LogEventLevel from config | App crashes on startup | Wrap Enum.Parse in try/catch; default to Information |
| File sink permissions denied | Logs not saved | Serilog self-log shows error; app continues with console sink |
| MAUI app lifecycle differs by platform | CleanUp override may not be called on all platforms | Primary reliance on AppDomain handler, not lifecycle events |

---

## Definition of Done

- [ ] All acceptance criteria implemented (AC1-AC8)
- [ ] Serilog configuration includes:
  - Configurable minimum level from appsettings.json
  - File size limit (100 MB)
  - SourceContext enrichment
  - Log directory auto-creation
- [ ] Global exception handlers registered:
  - AppDomain.UnhandledException (Fatal log, CloseAndFlush)
  - TaskScheduler.UnobservedTaskException (Error log, SetObserved)
- [ ] Manual tests completed:
  - Unhandled exception logged at Fatal level
  - Unobserved task exception logged at Error level, app continues
  - Log level configuration works (Debug entries appear when configured)
- [ ] Build successful
- [ ] Log files created in correct location

---

## Notes

**What's Already Done:**
Looking at the current MauiProgram.cs (lines 24-33), Serilog is already configured with:
- ✅ File sink at AppDataDirectory/logs/
- ✅ Console sink
- ✅ Rolling daily files
- ✅ 31-day retention
- ❌ Missing: Configurable minimum level
- ❌ Missing: File size limit
- ❌ Missing: SourceContext enrichment
- ❌ Missing: Global exception handlers

**What This Story Adds:**
This story completes the logging infrastructure by:
1. Making log level configurable from appsettings.json
2. Adding file size rollover (100 MB limit)
3. Adding SourceContext for better log organization
4. Adding global exception handlers for resilience

**Testing Approach:**
Manual verification is most appropriate because:
- Triggering real exceptions is the only way to verify handlers work
- Log file inspection confirms configuration
- Unit testing exception handlers is complex (requires mocking AppDomain events)

**Integration with Other Stories:**
- All services (US0001, US0002, future stories) benefit from ILogger<T> injection
- Exception handlers ensure app resilience for US0004, US0005 operations
- Audit logging supports troubleshooting for IT Admin Ian
