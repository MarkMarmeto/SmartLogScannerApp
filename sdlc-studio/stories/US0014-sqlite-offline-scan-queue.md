# US0014: Implement SQLite Offline Scan Queue

> **Status:** Done
> **Epic:** [EP0004: Offline Resilience and Sync](../epics/EP0004-offline-resilience-and-sync.md)
> **Owner:** AI Assistant
> **Reviewer:** Unassigned
> **Created:** 2026-02-13
> **Completed:** 2026-02-13

## User Story

**As a** system (benefiting Guard Gary indirectly)
**I want** a SQLite-backed offline scan queue with EF Core persistence and a well-defined queue service interface
**So that** every validated scan is durably stored locally before any network submission attempt, ensuring zero data loss during outages and giving Guard Gary confidence that no scan is ever lost

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency, needs instant visual feedback (green = good, red = bad). He must never worry about losing scans during network outages.
[Full persona details](../personas.md#guard-gary)

### Background
SmartLog Scanner must guarantee zero data loss regardless of network conditions. This story creates the foundational persistence layer: a SQLite database using EF Core with the QueuedScan entity, stored at `FileSystem.AppDataDirectory/scanner_queue.db`. The IOfflineQueueService interface provides queue CRUD operations consumed by the scan processing pipeline (EP0003) and the background sync service (US0016). When the server is unreachable, validated scans are enqueued with SyncStatus = "PENDING" and Guard Gary sees blue "Scan queued (offline)" feedback. This is the data backbone that every offline resilience feature depends on.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Availability | Must function fully offline; zero data loss | Every validated scan must be persistable to SQLite queue |
| PRD | Scalability | SQLite queue supports 10,000+ pending scans | EF Core queries must perform well with large datasets; index SyncStatus column |
| TRD | Tech Stack | SQLite via EF Core (Microsoft.EntityFrameworkCore.Sqlite) | Must use EF Core migrations, not raw SQL |
| TRD | Architecture | Interface-based design for testability | IOfflineQueueService interface with concrete implementation; DI registration |
| TRD | Tech Stack | FileSystem.AppDataDirectory for database path | SQLite database must be located at FileSystem.AppDataDirectory/scanner_queue.db |
| EP0004 | Architecture | ScannerDbContext registered as scoped service | Scoped lifetime to allow per-operation database context disposal |

---

## Acceptance Criteria

### AC1: QueuedScan Entity Defined with All Required Columns
- **Given** the EF Core data model is configured
- **When** the ScannerDbContext is inspected
- **Then** a QueuedScan entity exists with the following columns:
  - Id (int, primary key, auto-increment)
  - QrPayload (string, NOT NULL)
  - ScannedAt (string, NOT NULL) — ISO 8601 UTC timestamp of when the scan occurred
  - ScanType (string, NOT NULL) — "ENTRY" or "EXIT"
  - CreatedAt (string, NOT NULL) — ISO 8601 UTC timestamp of when the record was created
  - SyncStatus (string, default "PENDING") — one of "PENDING", "SYNCED", "FAILED"
  - SyncAttempts (int, default 0)
  - LastSyncError (string, nullable)
  - ServerScanId (string, nullable) — populated after successful server sync

### AC2: ScannerDbContext Registered as Scoped Service in DI
- **Given** MauiProgram.cs configures the DI container
- **When** the app starts
- **Then** ScannerDbContext is registered as a scoped service using `AddDbContext<ScannerDbContext>()` with the SQLite connection string pointing to `FileSystem.AppDataDirectory/scanner_queue.db`, and is resolvable via constructor injection

### AC3: SQLite Database Created at Correct Path
- **Given** the application launches for the first time on macOS or Windows
- **When** the database initialization runs
- **Then** a SQLite database file is created at `FileSystem.AppDataDirectory/scanner_queue.db` with the QueuedScans table matching the entity schema

### AC4: EF Core Migrations Run at App Startup
- **Given** the application starts (first launch or subsequent launch with pending migrations)
- **When** the startup initialization completes
- **Then** EF Core migrations are applied automatically via `context.Database.MigrateAsync()`, ensuring the database schema is up to date before any queue operations occur

### AC5: IOfflineQueueService EnqueueAsync Persists Scan
- **Given** a validated scan with QrPayload = "SMARTLOG:STU-2026-001:1706918400:abc123==", ScannedAt = "2026-02-13T08:30:00Z", and ScanType = "ENTRY"
- **When** IOfflineQueueService.EnqueueAsync(scan) is called
- **Then** a new QueuedScan row is inserted with SyncStatus = "PENDING", SyncAttempts = 0, CreatedAt set to the current UTC timestamp, LastSyncError = null, and ServerScanId = null, and the method returns the generated Id

### AC6: IOfflineQueueService GetPendingAsync Returns FIFO Batch
- **Given** the queue contains 120 scans with SyncStatus = "PENDING", ordered by CreatedAt ascending
- **When** IOfflineQueueService.GetPendingAsync(batchSize: 50) is called
- **Then** exactly 50 QueuedScan records are returned, ordered by CreatedAt ascending (oldest first, FIFO)

### AC7: IOfflineQueueService MarkSyncedAsync Updates Status
- **Given** a QueuedScan with Id = 42 exists with SyncStatus = "PENDING"
- **When** IOfflineQueueService.MarkSyncedAsync(id: 42, serverScanId: "srv-scan-abc-123") is called
- **Then** the record's SyncStatus is updated to "SYNCED" and ServerScanId is set to "srv-scan-abc-123"

### AC8: IOfflineQueueService MarkFailedAsync Increments Attempts
- **Given** a QueuedScan with Id = 42, SyncAttempts = 3, and SyncStatus = "PENDING"
- **When** IOfflineQueueService.MarkFailedAsync(id: 42, error: "HTTP 500: Internal Server Error") is called
- **Then** SyncAttempts is incremented to 4, LastSyncError is set to "HTTP 500: Internal Server Error", and SyncStatus remains "PENDING"

### AC9: IOfflineQueueService GetPendingCountAsync Returns Count
- **Given** the queue contains 75 scans with SyncStatus = "PENDING", 20 with "SYNCED", and 5 with "FAILED"
- **When** IOfflineQueueService.GetPendingCountAsync() is called
- **Then** the method returns 75

### AC10: Offline Scan Feedback Integration
- **Given** the scan processing pipeline determines the server is unreachable
- **When** a validated scan is enqueued via IOfflineQueueService
- **Then** the feedback system displays blue "Scan queued (offline)" to Guard Gary, and the queue pending count in the statistics footer is updated

---

## Scope

### In Scope
- QueuedScan entity class with EF Core data annotations/Fluent API configuration
- ScannerDbContext with DbSet<QueuedScan> and OnModelCreating configuration
- Index on SyncStatus column for query performance
- IOfflineQueueService interface: EnqueueAsync, GetPendingAsync, MarkSyncedAsync, MarkFailedAsync, GetPendingCountAsync
- OfflineQueueService concrete implementation
- DI registration in MauiProgram.cs (ScannerDbContext as scoped, IOfflineQueueService as scoped)
- EF Core migration for initial QueuedScan table creation
- Database migration execution at app startup
- Unit tests with in-memory SQLite provider

### Out of Scope
- Background sync logic (covered by US0016)
- Health check monitoring (covered by US0015)
- Manual queue management UI (view/edit/delete individual entries)
- Queue data export functionality
- FAILED scan retry/requeue mechanism
- Database encryption at rest
- Database backup/restore

---

## Technical Notes

### Entity Definition

```csharp
public class QueuedScan
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public string QrPayload { get; set; } = string.Empty;

    [Required]
    public string ScannedAt { get; set; } = string.Empty;

    [Required]
    public string ScanType { get; set; } = string.Empty;

    [Required]
    public string CreatedAt { get; set; } = string.Empty;

    public string SyncStatus { get; set; } = "PENDING";

    public int SyncAttempts { get; set; } = 0;

    public string? LastSyncError { get; set; }

    public string? ServerScanId { get; set; }
}
```

### DbContext

```csharp
public class ScannerDbContext : DbContext
{
    public DbSet<QueuedScan> QueuedScans => Set<QueuedScan>();

    public ScannerDbContext(DbContextOptions<ScannerDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QueuedScan>(entity =>
        {
            entity.HasIndex(e => e.SyncStatus);
            entity.Property(e => e.SyncStatus).HasDefaultValue("PENDING");
            entity.Property(e => e.SyncAttempts).HasDefaultValue(0);
        });
    }
}
```

### Service Interface

```csharp
public interface IOfflineQueueService
{
    Task<int> EnqueueAsync(QueuedScan scan);
    Task<List<QueuedScan>> GetPendingAsync(int batchSize);
    Task MarkSyncedAsync(int id, string serverScanId);
    Task MarkFailedAsync(int id, string error);
    Task<int> GetPendingCountAsync();
}
```

### DI Registration

```csharp
// In MauiProgram.cs
var dbPath = Path.Combine(FileSystem.AppDataDirectory, "scanner_queue.db");
builder.Services.AddDbContext<ScannerDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<IOfflineQueueService, OfflineQueueService>();
```

### Key Implementation Notes
- Use `context.Database.MigrateAsync()` at startup (not `EnsureCreated`) to support future schema changes
- GetPendingAsync must filter by `SyncStatus == "PENDING"` and order by `CreatedAt` ascending
- Timestamps stored as ISO 8601 UTC strings (e.g., "2026-02-13T08:30:00Z") for cross-platform consistency
- SyncStatus index ensures efficient filtering on large queues

### Data Requirements

**QueuedScan Entity Schema:**

| Column | CLR Type | DB Type | Constraints | Default | Description |
|--------|----------|---------|-------------|---------|-------------|
| Id | int | INTEGER | PRIMARY KEY, AUTO-INCREMENT | — | Unique row identifier |
| QrPayload | string | TEXT | NOT NULL | — | Full QR payload string (e.g., "SMARTLOG:STU-001:1706918400:hmac==") |
| ScannedAt | string | TEXT | NOT NULL | — | ISO 8601 UTC timestamp of when the QR was scanned |
| ScanType | string | TEXT | NOT NULL | — | "ENTRY" or "EXIT" |
| CreatedAt | string | TEXT | NOT NULL | — | ISO 8601 UTC timestamp of when the queue record was created |
| SyncStatus | string | TEXT | NOT NULL | "PENDING" | One of: "PENDING", "SYNCED", "FAILED" |
| SyncAttempts | int | INTEGER | NOT NULL | 0 | Number of sync attempts made |
| LastSyncError | string? | TEXT | NULLABLE | NULL | Error message from last failed sync attempt |
| ServerScanId | string? | TEXT | NULLABLE | NULL | Server-assigned scan ID after successful sync |

**Indexes:**

| Index | Column(s) | Purpose |
|-------|-----------|---------|
| IX_QueuedScans_SyncStatus | SyncStatus | Fast filtering of PENDING scans for sync batches |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Database file does not exist (first run) | EF Core MigrateAsync creates the database file and applies all migrations; app starts normally |
| Database locked during concurrent access (scan enqueue + sync read simultaneously) | SQLite WAL mode or EF Core retry logic handles the lock; operation retries up to 3 times with 100ms delay; if still locked, log error via Serilog and throw OfflineQueueException |
| Disk full when enqueuing a scan | EnqueueAsync catches the SqliteException, logs "Disk full: unable to enqueue scan" via Serilog, and throws OfflineQueueException with a descriptive message; caller surfaces error to Guard Gary via red feedback |
| Corrupt database file (checksum mismatch or invalid header) | MigrateAsync catches SqliteException; logs "Database corrupt: scanner_queue.db" via Serilog; deletes the corrupt file and recreates the database from migrations; logs a warning that previous queued data is lost |
| Very large queue (10,000+ entries) | GetPendingAsync uses indexed SyncStatus column and LIMIT clause (batchSize parameter) to avoid loading entire table; GetPendingCountAsync uses COUNT query, not materialization; performance remains under 100ms for both operations |
| Null or empty QrPayload passed to EnqueueAsync | EnqueueAsync validates input and throws ArgumentException("QrPayload cannot be null or empty") before database insert |
| Duplicate QrPayload enqueued (same QR scanned twice while offline) | Duplicates are allowed and enqueued as separate rows; deduplication is the server's responsibility; each enqueued scan gets a unique Id and independent SyncStatus |
| Migration failure on startup (e.g., incompatible schema change) | MigrateAsync exception is caught; error logged via Serilog with full stack trace; app displays an error state to IT Admin Ian and does not proceed to scanning mode |

---

## Test Scenarios

- [ ] EnqueueAsync inserts a QueuedScan with SyncStatus = "PENDING", SyncAttempts = 0, and returns the generated Id
- [ ] EnqueueAsync sets CreatedAt to current UTC timestamp in ISO 8601 format
- [ ] EnqueueAsync with null QrPayload throws ArgumentException
- [ ] EnqueueAsync with empty string QrPayload throws ArgumentException
- [ ] GetPendingAsync(50) returns up to 50 records with SyncStatus = "PENDING" ordered by CreatedAt ascending
- [ ] GetPendingAsync returns empty list when no PENDING scans exist
- [ ] GetPendingAsync does not return scans with SyncStatus = "SYNCED" or "FAILED"
- [ ] GetPendingAsync(10) returns only 5 records when only 5 PENDING scans exist
- [ ] MarkSyncedAsync updates SyncStatus to "SYNCED" and sets ServerScanId
- [ ] MarkSyncedAsync with non-existent Id throws InvalidOperationException
- [ ] MarkFailedAsync increments SyncAttempts by 1 and sets LastSyncError
- [ ] MarkFailedAsync preserves SyncStatus as "PENDING" (does not change to "FAILED" — that is the sync service's responsibility)
- [ ] GetPendingCountAsync returns correct count of PENDING scans only
- [ ] GetPendingCountAsync returns 0 when queue is empty
- [ ] ScannerDbContext creates QueuedScans table with all columns via MigrateAsync
- [ ] QueuedScan default values: SyncStatus = "PENDING", SyncAttempts = 0, LastSyncError = null, ServerScanId = null
- [ ] Duplicate QrPayload values are stored as separate rows with unique Ids
- [ ] IOfflineQueueService is resolvable from the DI container

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0003 | Soft Dependency | Serilog logging infrastructure for error logging | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Microsoft.EntityFrameworkCore.Sqlite | NuGet Package | Available for .NET 8.0 |
| Microsoft.EntityFrameworkCore.Design | NuGet Package (dev) | Available for .NET 8.0; needed for migrations tooling |
| MAUI FileSystem.AppDataDirectory | Platform SDK | Available in .NET 8.0 MAUI on macOS and Windows |

---

## Estimation

**Story Points:** 5
**Complexity:** Medium

**Rationale:** EF Core setup with SQLite in MAUI is well-documented but requires careful configuration (database path, migrations, DI registration). The IOfflineQueueService interface is straightforward CRUD. Complexity comes from edge case handling (concurrent access, corrupt database, disk full) and ensuring the SyncStatus index is properly configured for large queue performance.

---

## Open Questions

- [ ] Should the database use WAL (Write-Ahead Logging) mode for better concurrent read/write performance, or is the default journal mode sufficient? - Owner: Architect
- [ ] Should MarkFailedAsync also update SyncStatus to "FAILED" when SyncAttempts reaches the max (10), or should that logic remain in the sync service (US0016)? - Owner: Tech Lead
- [ ] Is there a maximum age for PENDING scans before they should be considered stale and auto-marked as FAILED? - Owner: Product

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
