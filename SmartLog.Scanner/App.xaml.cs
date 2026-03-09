using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.Views;

namespace SmartLog.Scanner;

public partial class App : Application
{
	private readonly SecurityMigrationService _securityMigration;
	private readonly DatabaseInitializationService _databaseInit;
	private readonly IBackgroundSyncService _backgroundSync;
	private readonly ILogger<App> _logger;

	public App(
		SecurityMigrationService securityMigration,
		DatabaseInitializationService databaseInit,
		IBackgroundSyncService backgroundSync,
		ILogger<App> logger)
	{
		InitializeComponent();

		_securityMigration = securityMigration;
		_databaseInit = databaseInit;
		_backgroundSync = backgroundSync;
		_logger = logger;

		// Start with AppShell - it will handle navigation to setup if needed
		MainPage = new AppShell();
	}

	protected override async void OnStart()
	{
		base.OnStart();

		// SECURITY FIX (CRITICAL-01): Migrate secrets from config.json to SecureStorage
		// This runs on every app start but is idempotent (safe to run multiple times)
		try
		{
			await _securityMigration.MigrateSecretsAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Security migration failed");
			System.Diagnostics.Debug.WriteLine($"Security migration failed: {ex.Message}");
		}

		// US0014 AC7: Initialize SQLite database on app startup
		try
		{
			await _databaseInit.InitializeAsync();
		}
		catch (Exception ex)
		{
			// Log error but don't crash the app
			System.Diagnostics.Debug.WriteLine($"Database initialization failed: {ex.Message}");
		}

		// US0016: Start background sync service
		try
		{
			await _backgroundSync.StartAsync();
		}
		catch (Exception ex)
		{
			// Log error but don't crash the app
			System.Diagnostics.Debug.WriteLine($"Background sync service failed to start: {ex.Message}");
		}
	}
}
