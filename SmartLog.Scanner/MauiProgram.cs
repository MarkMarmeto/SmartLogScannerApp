using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using CommunityToolkit.Maui;
using Polly;
using Serilog;
using Serilog.Events;
using Plugin.Maui.Audio;
using SmartLog.Scanner.Core.Data;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.Core.ViewModels;
using SmartLog.Scanner.ViewModels;

namespace SmartLog.Scanner;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
			.ConfigureMauiHandlers(handlers =>
			{
#if MACCATALYST
				handlers.AddHandler<Controls.CameraQrView, Platforms.MacCatalyst.CameraQrViewHandler>();
#endif
			});

		// US0002/US0003: Load configuration from appsettings.json FIRST
		var appSettingsStream = FileSystem.OpenAppPackageFileAsync("appsettings.json").Result;
		var config = new ConfigurationBuilder()
			.AddJsonStream(appSettingsStream)
			.Build();
		builder.Configuration.AddConfiguration(config);

		// US0003: Configure Serilog with settings from appsettings.json
		// AC1/AC3: Create log directory if not exists
		var logDir = Path.Combine(FileSystem.AppDataDirectory, "logs");
		Directory.CreateDirectory(logDir);

		var logPath = Path.Combine(logDir, "smartlog-scanner-.log");

		// AC6/AC7: Load minimum level from config (default: Information)
		var minLevelStr = config.GetValue<string>("Logging:MinimumLevel", "Information") ?? "Information";
		var minLevel = LogEventLevel.Information;
		try
		{
			minLevel = Enum.Parse<LogEventLevel>(minLevelStr, ignoreCase: true);
		}
		catch (ArgumentException)
		{
			// Invalid level in config, default to Information
			minLevel = LogEventLevel.Information;
		}

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Is(minLevel)
			.WriteTo.File(
				path: logPath,
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 31,
				fileSizeLimitBytes: 100_000_000, // AC3: 100 MB limit
				rollOnFileSizeLimit: true,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
			.WriteTo.Console(
				outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.Enrich.FromLogContext() // AC2: SourceContext enrichment
			.CreateLogger();

		builder.Logging.AddSerilog(Log.Logger);

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// US0003: Register global exception handlers (AC4, AC5)
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

		// SECURITY: Certificate validation setting from appsettings.json
		// Defaults to false (production-safe). User explicitly enables via setup UI.
		// Note: This reads from appsettings.json; actual runtime value comes from config.json
		// via user's setup choices (saved in FileConfigService).
		var acceptSelfSigned = config.GetValue<bool>("Server:AcceptSelfSignedCerts", false);
		var timeoutSeconds = config.GetValue<int>("Server:TimeoutSeconds", 30);

		// SECURITY: Log warning if self-signed certificates would be accepted (by default)
		if (acceptSelfSigned)
		{
			Log.Warning("⚠️ Self-signed TLS certificate acceptance is enabled in appsettings.json. " +
				"User can override this in setup wizard.");
		}

		// US0002: Register named HttpClient "SmartLogApi" with resilience policies (AC1-AC8)
		builder.Services.AddHttpClient("SmartLogApi")
			.ConfigurePrimaryHttpMessageHandler(() =>
			{
				var handler = new HttpClientHandler();
				if (acceptSelfSigned)
				{
					// AC2: Accept self-signed certificates when flag is true
					handler.ServerCertificateCustomValidationCallback =
						HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
				}
				// AC3: Use default validation when flag is false (no callback set)
				return handler;
			})
			.ConfigureHttpClient(client =>
			{
				// AC7: Configure timeout from appsettings.json
				client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
			})
			// AC5: Polly retry policy - 3 retries with exponential backoff (1s, 2s, 4s)
			.AddTransientHttpErrorPolicy(policy =>
				policy.WaitAndRetryAsync(
					retryCount: 3,
					sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1)),
					onRetry: (outcome, timespan, retryCount, context) =>
					{
						Log.Warning("HTTP request retry {RetryCount} after {Delay}s due to {Exception}",
							retryCount, timespan.TotalSeconds, outcome.Exception?.GetType().Name ?? "transient error");
					}))
			// AC6: Polly circuit breaker - opens after 5 failures for 30 seconds
			.AddTransientHttpErrorPolicy(policy =>
				policy.CircuitBreakerAsync(
					handledEventsAllowedBeforeBreaking: 5,
					durationOfBreak: TimeSpan.FromSeconds(30),
					onBreak: (outcome, duration) =>
					{
						Log.Error("HTTP circuit breaker opened for {Duration}s due to {Exception}",
							duration.TotalSeconds, outcome.Exception?.GetType().Name ?? "transient errors");
					},
					onReset: () =>
					{
						Log.Information("HTTP circuit breaker reset (closed)");
					},
					onHalfOpen: () =>
					{
						Log.Information("HTTP circuit breaker half-open (testing)");
					}));

		// US0001: Register configuration services (AC4)
		builder.Services.AddSingleton<ISecureConfigService, SecureConfigService>();
		builder.Services.AddSingleton<IPreferencesService, PreferencesService>();
		builder.Services.AddSingleton<Core.Services.FileConfigService>();

		// Device detection service (automatic camera/USB detection)
		builder.Services.AddSingleton<IDeviceDetectionService, Platforms.MacCatalyst.DeviceDetectionService>();

		// US0004: Register navigation service and ViewModels
		builder.Services.AddSingleton<INavigationService, Services.ShellNavigationService>();
		builder.Services.AddTransient<SetupViewModel>();
		builder.Services.AddTransient<Views.SetupPage>();

		// US0005: Register connection test service
		builder.Services.AddSingleton<IConnectionTestService, ConnectionTestService>();

		// US0006: Register HMAC QR validation service
		builder.Services.AddSingleton<IHmacValidator, HmacValidator>();

		// Register scan deduplication service (student-level dedup with tiered time windows)
		builder.Services.AddSingleton<IScanDeduplicationService, ScanDeduplicationService>();

		// US0007: Register camera QR scanner service
		builder.Services.AddSingleton<CameraQrScannerService>();

		// US0008: Register USB keyboard wedge scanner service
		builder.Services.AddSingleton<UsbQrScannerService>();

		// US0010: Register scan submission service
		builder.Services.AddSingleton<IScanApiService, ScanApiService>();

		// US0014: Register SQLite database for offline queue
		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "smartlog-scanner.db");
		builder.Services.AddDbContextFactory<ScannerDbContext>(options =>
			options.UseSqlite($"Data Source={dbPath}"));

		// US0014: Register offline queue service with SQLite persistence
		builder.Services.AddSingleton<IOfflineQueueService, OfflineQueueService>();

		// US0014: Register database initialization service
		builder.Services.AddSingleton<DatabaseInitializationService>();

		// US0015: Register health check monitoring service
		builder.Services.AddSingleton<IHealthCheckService, HealthCheckService>();

		// US0016: Register background sync service
		builder.Services.AddSingleton<IBackgroundSyncService, BackgroundSyncService>();

		// US0012: Register audio services
		builder.Services.AddSingleton(AudioManager.Current);
		builder.Services.AddSingleton<ISoundService, Services.SoundService>();

		// Register ViewModel and Page
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddTransient<Views.MainPage>(); // Register page after ViewModel

		return builder.Build();
	}
}
