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
    /// Creates database if it doesn't exist and applies any pending migrations.
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
                // Database exists - check for pending migrations
                var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                if (pendingMigrations.Any())
                {
                    _logger.LogInformation("Applying {Count} pending migrations...", pendingMigrations.Count());
                    await context.Database.MigrateAsync();
                    _logger.LogInformation("Migrations applied successfully");
                }
                else
                {
                    _logger.LogInformation("Database is up to date");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }
}
