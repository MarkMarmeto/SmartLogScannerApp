# US0003: Implement Global Exception Handling and Logging

> **Status:** Done
> **Epic:** [EP0001: Device Setup and Configuration](../epics/EP0001-device-setup-and-configuration.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As a** system (benefiting IT Admin Ian for diagnostics)
**I want** structured logging via Serilog and global exception handlers that capture all unhandled exceptions
**So that** the application recovers gracefully from crashes, all operations are logged for audit and troubleshooting, and IT Admin Ian can diagnose issues from log files without needing to reproduce the problem on-site

## Context

### Persona Reference
**IT Admin Ian** - School IT administrator, intermediate technical proficiency, troubleshoots scanner issues remotely. Needs log files that provide clear, timestamped records of application behavior and errors.
[Full persona details](../personas.md#it-admin-ian)

### Background
SmartLog Scanner runs unattended on school gate machines. When Guard Gary encounters an issue, he calls IT Admin Ian. Ian needs comprehensive log files to diagnose problems without physically visiting the gate machine. Additionally, the application must recover gracefully from unexpected crashes -- AppDomain.UnhandledException and TaskScheduler.UnobservedTaskException must be caught, logged, and handled so the scanner remains operational. This story establishes the logging and exception handling infrastructure that every other service in the application depends on for observability and resilience.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Security | No secrets in log files | Serilog configuration must not include destructuring or templates that could capture API keys or HMAC secrets |
| PRD | Availability | App recovers gracefully from crashes; pending scans preserved | Global exception handlers must log and attempt recovery, not terminate the process |
| TRD | Tech Stack | Serilog with file + console sinks | Configure both sinks in MauiProgram.cs |
| TRD | Infrastructure | Log files at FileSystem.AppDataDirectory/logs/ | File sink path must use cross-platform FileSystem.AppDataDirectory |
| PRD | Audit | All scan operations logged for audit/troubleshooting | Logging infrastructure must be available before any scan operations begin |

---

## Acceptance Criteria

### AC1: Serilog configured with file sink
- **Given** the application starts
- **When** Serilog is initialized in MauiProgram.cs
- **Then** a file sink is configured to write log entries to `{FileSystem.AppDataDirectory}/logs/smartlog-scanner-.log` (with date-based rolling file name, e.g., `smartlog-scanner-20260213.log`)

### AC2: Serilog configured with console sink
- **Given** the application starts in a debug or development context
- **When** Serilog is initialized
- **Then** a console sink is configured to write log entries to standard output with the same structured format as the file sink

### AC3: Rolling file policy configured
- **Given** the file sink is active and the application runs over multiple days
- **When** a new day begins (midnight rollover)
- **Then** a new log file is created with the current date in the filename (e.g., `smartlog-scanner-20260214.log`), and log files older than 31 days are automatically deleted to prevent unbounded disk usage

### AC4: AppDomain.UnhandledException captured and logged
- **Given** an unhandled exception occurs on any thread (e.g., a NullReferenceException in a service method)
- **When** the exception propagates to AppDomain.CurrentDomain.UnhandledException
- **Then** the exception is logged via Serilog at Fatal level with the full exception message, stack trace, and the string "Unhandled AppDomain exception", and the application attempts graceful shutdown

### AC5: TaskScheduler.UnobservedTaskException captured and logged
- **Given** an exception occurs in an async Task that is not awaited or observed
- **When** the garbage collector detects the unobserved exception and raises TaskScheduler.UnobservedTaskException
- **Then** the exception is logged via Serilog at Error level with the full exception message and stack trace, the exception is marked as observed via SetObserved() to prevent process termination, and the application continues running

### AC6: Minimum log level configurable via appsettings.json
- **Given** appsettings.json contains `"Logging": { "MinimumLevel": "Debug" }`
- **When** the application starts and Serilog is configured
- **Then** log entries at Debug level and above are written to both sinks, and entries below the configured level are discarded

### AC7: Default minimum log level is Information
- **Given** appsettings.json does not contain a MinimumLevel key, or appsettings.json is missing
- **When** the application starts
- **Then** the default minimum log level is Information, meaning Debug and Verbose entries are discarded

### AC8: Application recovers gracefully from crashes
- **Given** an unhandled exception triggers the global AppDomain handler
- **When** the exception is logged
- **Then** the application state is preserved (no corruption of Preferences, SecureStorage, or pending offline scans in SQLite), and on the next launch the application starts normally with all previously persisted data intact

---

## Scope

### In Scope
- Serilog initialization in MauiProgram.cs with file and console sinks
- File sink at `FileSystem.AppDataDirectory/logs/` with date-based rolling files
- Rolling file retention: 31 days
- File size limit: 100 MB per file (with rollover)
- Structured log output format with timestamp, level, source context, and message
- AppDomain.CurrentDomain.UnhandledException handler registration
- TaskScheduler.UnobservedTaskException handler registration
- Configurable minimum log level from appsettings.json
- Serilog integration with Microsoft.Extensions.Logging (ILogger<T> resolution via DI)
- Log directory creation if it does not exist
- Unit tests for exception handler registration and log level configuration

### Out of Scope
- Remote log aggregation (e.g., Seq, Elasticsearch)
- Log viewer UI within the application
- Log file encryption
- Structured event correlation IDs
- Performance profiling or metrics collection
- Platform-specific crash reporting (macOS crash reports, Windows Error Reporting)
- Log rotation based on file count (only date and size-based)

---

## Technical Notes

### Implementation Details

**Serilog initialization in MauiProgram.cs:**
```csharp
var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs", "smartlog-scanner-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(config.GetValue<LogEventLevel>("Logging:MinimumLevel", LogEventLevel.Information))
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31,
        fileSizeLimitBytes: 100_000_000,
        rollOnFileSizeLimit: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Console(
        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Logging.AddSerilog(Log.Logger);
```

**Global exception handlers in App.xaml.cs or MauiProgram.cs:**
```csharp
AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    var exception = args.ExceptionObject as Exception;
    Log.Fatal(exception, "Unhandled AppDomain exception. IsTerminating: {IsTerminating}", args.IsTerminating);
    Log.CloseAndFlush();
};

TaskScheduler.UnobservedTaskException += (sender, args) =>
{
    Log.Error(args.Exception, "Unobserved task exception");
    args.SetObserved();
};
```

**Log directory creation:**
```csharp
var logDir = Path.Combine(FileSystem.AppDataDirectory, "logs");
Directory.CreateDirectory(logDir); // No-op if exists
```

**Important:** Global exception handlers must be registered as early as possible in the application lifecycle -- before any DI container resolution or page navigation.

### API Contracts
Not applicable (no HTTP calls in this story).

### Data Requirements

**Log file structure:**

| Property | Value |
|----------|-------|
| Directory | `{FileSystem.AppDataDirectory}/logs/` |
| File pattern | `smartlog-scanner-YYYYMMDD.log` |
| Rolling interval | Daily |
| Retention | 31 days |
| Max file size | 100 MB (rolls to new file on limit) |
| Encoding | UTF-8 |

**appsettings.json logging configuration:**

| Key Path | Type | Default | Description |
|----------|------|---------|-------------|
| Logging:MinimumLevel | string (LogEventLevel) | "Information" | Minimum Serilog log level |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Log directory not writable (permissions denied on macOS or Windows) | Serilog's self-log captures the error to console; file sink silently fails; console sink continues to work; application does not crash |
| Disk full: no space to write log files | Serilog file sink fails silently (Serilog does not throw from sinks); console sink continues; application continues running without file logging |
| Concurrent logging from multiple threads (UI thread, background sync thread, timer callbacks) | Serilog file sink is thread-safe; entries are interleaved but never corrupted; each entry is a complete line |
| Exception during exception handling (e.g., Serilog fails to log the exception in the global handler) | Handler is wrapped in try/catch; inner exception is written to console via Serilog self-log; application attempts to continue |
| App restart after crash | On next launch, Serilog creates or appends to the current day's log file; previous log files remain intact; no data corruption |
| Very rapid log writes (e.g., 1000+ log entries per second during scan burst) | Serilog buffers writes; file sink handles high throughput; no significant UI thread impact because logging is fire-and-forget |
| FileSystem.AppDataDirectory returns different paths on different platforms | Serilog uses the platform-specific path provided by MAUI FileSystem API; logs are always in the correct platform directory |
| appsettings.json contains invalid MinimumLevel value (e.g., "SuperDebug") | Serilog defaults to Information level; warning logged about invalid configuration value |
| Log file exceeds 100 MB within a single day | Serilog rolls to a new file (e.g., smartlog-scanner-20260213_001.log) while keeping the original |

---

## Test Scenarios

- [ ] Serilog Logger is initialized and resolvable via ILogger<T> from DI container
- [ ] Log entries written to file at `FileSystem.AppDataDirectory/logs/` directory
- [ ] Log file name follows pattern `smartlog-scanner-YYYYMMDD.log`
- [ ] Console sink outputs log entries to standard output
- [ ] Log output includes timestamp, level, source context, and message
- [ ] Minimum log level "Information" filters out Debug and Verbose entries
- [ ] Minimum log level "Debug" includes Debug entries in output
- [ ] Custom minimum level from appsettings.json is applied correctly
- [ ] Default minimum level is Information when appsettings.json key is absent
- [ ] AppDomain.UnhandledException handler is registered at startup
- [ ] TaskScheduler.UnobservedTaskException handler is registered at startup
- [ ] Unhandled exception on background thread is captured and logged at Fatal level
- [ ] Unobserved task exception is captured, logged at Error level, and marked as observed
- [ ] Log directory is created automatically if it does not exist on startup
- [ ] Rolling file: new file created on new calendar day
- [ ] Log retention: files older than 31 days are automatically cleaned up
- [ ] Exception log entries include full stack trace
- [ ] Concurrent logging from multiple threads produces no corrupted entries
- [ ] Application does not crash when log directory is not writable (graceful degradation)
- [ ] Log.CloseAndFlush() is called during application shutdown to ensure all buffered entries are written

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| None | — | This is a foundational story with no story dependencies | — |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Serilog | NuGet Package | Available (latest) |
| Serilog.Sinks.File | NuGet Package | Available (latest) |
| Serilog.Sinks.Console | NuGet Package | Available (latest) |
| Serilog.Extensions.Logging | NuGet Package | Available (latest, integrates with Microsoft.Extensions.Logging) |
| Microsoft.Extensions.Configuration.Json | NuGet Package | Available in .NET 8.0 |
| MAUI FileSystem.AppDataDirectory | Platform SDK | Available in .NET 8.0 MAUI |

---

## Estimation

**Story Points:** 3
**Complexity:** Low

---

## Open Questions

- [ ] Should the application attempt to auto-restart after an AppDomain.UnhandledException, or just log and let the OS handle the process termination? - Owner: Architect
- [ ] Should log files be accessible to IT Admin Ian through a "Export Logs" button in a future settings page, or only via file system access? - Owner: Product
- [ ] Is there a need for sensitive data filtering/masking in Serilog (e.g., ensuring API keys passed as parameters to log methods are redacted)? - Owner: Architect

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
