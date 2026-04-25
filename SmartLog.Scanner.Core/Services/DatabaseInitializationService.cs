using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Data;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0014: Service to initialize SQLite database on app startup.
/// Ensures database is created and migrations are applied.
/// </summary>
public class DatabaseInitializationService
{
    private readonly IDbContextFactory<ScannerDbContext> _dbFactory;
    private readonly ILogger<DatabaseInitializationService> _logger;

    public DatabaseInitializationService(
        IDbContextFactory<ScannerDbContext> dbFactory,
        ILogger<DatabaseInitializationService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// US0014 AC7: Initialize database on app startup.
    /// Creates database if it doesn't exist, then ensures all tables exist (handles schema additions
    /// to existing databases that pre-date a given table being added to the model).
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            // Check if database exists
            var exists = await context.Database.CanConnectAsync();

            if (!exists)
            {
                _logger.LogInformation("Creating SQLite database...");
                await context.Database.EnsureCreatedAsync();
                _logger.LogInformation("SQLite database created successfully");
            }
            else
            {
                _logger.LogInformation("Database exists — ensuring schema is current");
            }

            // Always ensure all tables exist. EnsureCreatedAsync only runs on a new DB, so any
            // tables added to the model after the DB was first created must be created here.
            await EnsureTablesExistAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }

    /// <summary>
    /// Idempotent schema patch: creates any tables that may be missing from an existing database.
    /// Uses CREATE TABLE IF NOT EXISTS so it is safe to run on every startup.
    /// </summary>
    private async Task EnsureTablesExistAsync(SmartLog.Scanner.Core.Data.ScannerDbContext context)
    {
        // QueuedScans — present since initial release
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS QueuedScans (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                QrPayload TEXT NOT NULL,
                StudentId TEXT NOT NULL,
                ScannedAt TEXT NOT NULL,
                ScanType TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                SyncStatus TEXT NOT NULL DEFAULT 'PENDING',
                SyncAttempts INTEGER NOT NULL DEFAULT 0,
                LastSyncError TEXT,
                ServerScanId TEXT,
                LastAttemptAt TEXT,
                CameraIndex INTEGER,
                CameraName TEXT
            )");

        // ScanLogs — added after initial release; existing DBs may not have this table
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ScanLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                RawPayload TEXT NOT NULL,
                StudentId TEXT,
                StudentName TEXT,
                ScanType TEXT NOT NULL,
                Status INTEGER NOT NULL,
                Message TEXT,
                ScanId TEXT,
                NetworkAvailable INTEGER NOT NULL DEFAULT 0,
                ProcessingTimeMs INTEGER NOT NULL DEFAULT 0,
                GradeSection TEXT,
                ErrorDetails TEXT,
                ScanMethod TEXT NOT NULL DEFAULT 'Unknown',
                CameraIndex INTEGER,
                CameraName TEXT
            )");

        await context.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_QueuedScans_SyncStatus ON QueuedScans (SyncStatus)");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_QueuedScans_CreatedAt ON QueuedScans (CreatedAt)");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_QueuedScans_StudentId_ScanType_SyncStatus ON QueuedScans (StudentId, ScanType, SyncStatus)");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ScanLogs_Timestamp ON ScanLogs (Timestamp)");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ScanLogs_Status ON ScanLogs (Status)");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_ScanLogs_StudentId ON ScanLogs (StudentId)");

        // EP0011/US0090: Camera identity columns — added after initial release.
        // ALTER TABLE ADD COLUMN IF NOT EXISTS requires SQLite 3.37.0+ (bundled via EF Core 8).
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE QueuedScans ADD COLUMN IF NOT EXISTS CameraIndex INTEGER");
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE QueuedScans ADD COLUMN IF NOT EXISTS CameraName TEXT");
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE ScanLogs ADD COLUMN IF NOT EXISTS CameraIndex INTEGER");
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE ScanLogs ADD COLUMN IF NOT EXISTS CameraName TEXT");

        _logger.LogInformation("Schema check complete");
    }
}
