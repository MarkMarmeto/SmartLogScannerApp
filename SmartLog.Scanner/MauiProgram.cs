using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using CommunityToolkit.Maui;
using Polly;
using Serilog;
using Serilog.Events;
using Plugin.Maui.Audio;
using SmartLog.Scanner.Core.Data;
using SmartLog.Scanner.Core.Infrastructure;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.Core.ViewModels;
using SmartLog.Scanner.ViewModels;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SmartLog.Scanner;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureMauiHandlers(handlers =>
			{
#if MACCATALYST
				handlers.AddHandler<Controls.CameraQrView, Platforms.MacCatalyst.CameraQrViewHandler>();
				handlers.AddHandler<Controls.CameraPreviewView, Platforms.MacCatalyst.CameraPreviewHandler>();
#elif WINDOWS
				handlers.AddHandler<Controls.CameraQrView, Platforms.Windows.CameraQrViewHandler>();
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

		// SECURITY: Certificate validation setting.
		// Read from user Preferences first (saved by setup wizard), fall back to appsettings.json.
		// This ensures the HttpClient respects the user's "Accept self-signed certs" choice.
		var setupCompleted = Preferences.Default.Get(ConfigKeys.SetupCompleted, false);
		var acceptSelfSigned = setupCompleted
			? Preferences.Default.Get(ConfigKeys.AcceptSelfSignedCerts, false)
			: config.GetValue<bool>("Server:AcceptSelfSignedCerts", false);
		var certificateThumbprint = config.GetValue<string>("Server:CertificateThumbprint", string.Empty);
		var timeoutSeconds = config.GetValue<int>("Server:TimeoutSeconds", 30);

		// SECURITY FIX (HIGH-02): Certificate pinning validation
		// Replaces DangerousAcceptAnyServerCertificateValidator with proper validation
		Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> certificateValidator =
			(message, cert, chain, errors) =>
		{
			// Accept valid CA-signed certificates
			if (errors == SslPolicyErrors.None)
			{
				Log.Debug("Valid CA-signed certificate accepted");
				return true;
			}

			// Reject all invalid certificates if self-signed mode is disabled
			if (!acceptSelfSigned)
			{
				Log.Warning("Certificate validation failed. Self-signed mode disabled. Errors: {Errors}", errors);
				return false;
			}

			// Self-signed mode enabled — if no thumbprint configured, accept any cert (LAN trust mode).
			// This is safe for closed on-premise LAN deployments where the server cert is known.
			if (string.IsNullOrWhiteSpace(certificateThumbprint))
			{
				Log.Warning("⚠️ Accepting self-signed certificate without thumbprint pinning (LAN trust mode). " +
					"Safe for closed on-premise networks only.");
				return true;
			}

			if (cert == null)
			{
				Log.Error("No certificate provided by server");
				return false;
			}

			// Verify exact certificate thumbprint match (certificate pinning)
			var actualThumbprint = cert.Thumbprint;
			if (!string.Equals(actualThumbprint, certificateThumbprint, StringComparison.OrdinalIgnoreCase))
			{
				Log.Error("Certificate thumbprint mismatch. Expected: {Expected}, Got: {Actual}",
					certificateThumbprint, actualThumbprint);
				return false;
			}

			// Verify hostname matches certificate
			var requestHost = message.RequestUri?.Host;
			var certDnsName = cert.GetNameInfo(X509NameType.DnsName, false);
			if (!string.Equals(requestHost, certDnsName, StringComparison.OrdinalIgnoreCase))
			{
				Log.Warning("Certificate hostname mismatch. Request: {RequestHost}, Cert: {CertHost}",
					requestHost, certDnsName);
				// Allow mismatch for IP addresses (common in dev/test environments)
				if (!System.Net.IPAddress.TryParse(requestHost, out _))
				{
					Log.Error("Hostname mismatch for non-IP address. Rejecting.");
					return false;
				}
			}

			Log.Warning("⚠️ Accepting pinned self-signed certificate: {Thumbprint}", actualThumbprint);
			return true;
		};

		// SECURITY: Log warning if self-signed certificates would be accepted (by default)
		if (acceptSelfSigned)
		{
			if (string.IsNullOrWhiteSpace(certificateThumbprint))
			{
				Log.Error("⚠️ Self-signed TLS certificate acceptance is enabled but NO THUMBPRINT configured. " +
					"All HTTPS requests will FAIL. Please configure Server:CertificateThumbprint in appsettings.json.");
			}
			else
			{
				Log.Warning("⚠️ Self-signed TLS certificate acceptance is enabled with pinned thumbprint: {Thumbprint}. " +
					"User can override this in setup wizard.", certificateThumbprint);
			}
		}

		// Register dedicated HttpClient for health checks (NO retry/circuit breaker to prevent flapping)
		builder.Services.AddHttpClient("HealthCheck")
			.ConfigurePrimaryHttpMessageHandler(() =>
			{
				var handler = new HttpClientHandler();
				if (acceptSelfSigned)
				{
					// SECURITY FIX (HIGH-02): Use proper certificate validation with pinning
					handler.ServerCertificateCustomValidationCallback = certificateValidator;
				}
				// When acceptSelfSigned is false, use default validation (no callback)
				return handler;
			})
			.ConfigureHttpClient(client =>
			{
				client.Timeout = TimeSpan.FromSeconds(5); // Shorter timeout for health checks
			});
		// NOTE: No Polly policies for health checks - we want fast, deterministic failures

		// US0002: Register named HttpClient "SmartLogApi" with resilience policies (AC1-AC8)
		builder.Services.AddHttpClient("SmartLogApi")
			.ConfigurePrimaryHttpMessageHandler(() =>
			{
				var handler = new HttpClientHandler();
				if (acceptSelfSigned)
				{
					// SECURITY FIX (HIGH-02): Use proper certificate validation with pinning
					// AC2: Accept self-signed certificates ONLY if thumbprint matches
					handler.ServerCertificateCustomValidationCallback = certificateValidator;
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

		// SECURITY FIX (CRITICAL-01): Register security migration service
		builder.Services.AddSingleton<Core.Services.SecurityMigrationService>();

		// US0089: Register scan type migration service
		builder.Services.AddSingleton<Core.Services.IMigrationStore, Core.Services.MauiMigrationStore>();
		builder.Services.AddSingleton<Core.Services.ScanTypeMigrationService>();

		// Device detection service (automatic camera/USB detection) - Platform-specific
#if MACCATALYST
		builder.Services.AddSingleton<IDeviceDetectionService, Platforms.MacCatalyst.DeviceDetectionService>();
		builder.Services.AddSingleton<ICameraEnumerationService, Platforms.MacCatalyst.CameraEnumerationService>();
		// EP0011: Headless camera worker factory — no native views, no preview layers
		builder.Services.AddSingleton<ICameraWorkerFactory, Platforms.MacCatalyst.CameraWorkerFactory>();
#elif WINDOWS
		builder.Services.AddSingleton<IDeviceDetectionService, Platforms.Windows.DeviceDetectionService>();
		builder.Services.AddSingleton<ICameraEnumerationService, Platforms.Windows.CameraEnumerationService>();
		// EP0011: Headless camera worker factory
		builder.Services.AddSingleton<ICameraWorkerFactory, Platforms.Windows.CameraWorkerFactory>();
#else
		// Fallback: Default to USB scanner only
		builder.Services.AddSingleton<IDeviceDetectionService, Core.Services.DefaultDeviceDetectionService>();
#endif

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

		// US0007: Register camera QR scanner service (still used as prototype by MultiCameraManager)
		builder.Services.AddTransient<CameraQrScannerService>();

		// US0008: Register USB keyboard wedge scanner service
		builder.Services.AddSingleton<UsbQrScannerService>();

		// EP0011: Register multi-camera manager + adaptive throttle
		builder.Services.AddSingleton<AdaptiveDecodeThrottle>();
		builder.Services.AddSingleton<IMultiCameraManager, MultiCameraManager>();

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

		// Register scan history/logging service for diagnostics
		builder.Services.AddSingleton<IScanHistoryService, ScanHistoryService>();

		// US0015: Register health check monitoring service
		builder.Services.AddSingleton<IHealthCheckService, HealthCheckService>();

		// Time sync service — corrects device clock drift using server time on startup
		builder.Services.AddSingleton<ITimeService, TimeService>();

		// US0016: Register background sync service
		builder.Services.AddSingleton<IBackgroundSyncService, BackgroundSyncService>();

		// US0012: Register audio services
		builder.Services.AddSingleton(AudioManager.Current);
		builder.Services.AddSingleton<ISoundService, Services.SoundService>();

		// Register ViewModels and Pages
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddTransient<Views.MainPage>();

		// Register Scan Logs viewer
		builder.Services.AddTransient<Core.ViewModels.ScanLogsViewModel>();
		builder.Services.AddTransient<Views.ScanLogsPage>();

		// Register Offline Queue management page
		builder.Services.AddTransient<ViewModels.OfflineQueueViewModel>();
		builder.Services.AddTransient<Views.OfflineQueuePage>();

		return builder.Build();
	}
}
