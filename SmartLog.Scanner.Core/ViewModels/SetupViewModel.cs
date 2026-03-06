using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmartLog.Scanner.Core.ViewModels;

/// <summary>
/// US0004/US0005: ViewModel for device setup wizard page.
/// Handles form validation, connection testing, credential storage, and navigation.
/// </summary>
public partial class SetupViewModel : ObservableObject
{
	private readonly ISecureConfigService _secureConfig;
	private readonly IPreferencesService _preferences;
	private readonly FileConfigService _fileConfig;
	private readonly INavigationService _navigation;
	private readonly IConnectionTestService _connectionTestService;
	private readonly IDeviceDetectionService _deviceDetection;
	private readonly ILogger<SetupViewModel> _logger;

	// Form field properties
	[ObservableProperty] private string _serverUrl = string.Empty;
	[ObservableProperty] private string _apiKey = string.Empty;
	[ObservableProperty] private string _hmacSecret = string.Empty;
	[ObservableProperty] private string _selectedScanType = "ENTRY";

	/// <summary>
	/// SECURITY: Certificate validation setting (default: false for security).
	/// User must explicitly opt-in via checkbox with warning.
	/// </summary>
	[ObservableProperty] private bool _acceptSelfSignedCerts = false;

	// Device detection properties
	[ObservableProperty] private bool _isDetectingDevices;
	[ObservableProperty] private string _detectedDevicesMessage = "Detecting devices...";
	[ObservableProperty] private ScanningMethod _detectedScanMethod = ScanningMethod.None;

	// UI state
	[ObservableProperty] private bool _isEditMode;
	[ObservableProperty] private string _pageTitle = "Device Configuration";
	[ObservableProperty] private string _saveButtonText = "Complete Setup";

	// Error properties
	[ObservableProperty] private string? _serverUrlError;
	[ObservableProperty] private string? _apiKeyError;
	[ObservableProperty] private string? _hmacSecretError;
	[ObservableProperty] private string? _saveError;

	// UI state
	[ObservableProperty] private bool _isSaving;

	// US0005: Connection test properties
	[ObservableProperty] private bool _isTestingConnection;
	[ObservableProperty] private ConnectionTestResult _testResult = ConnectionTestResult.None;
	[ObservableProperty] private string? _testResultMessage;
	[ObservableProperty] private bool _isConnectionValid;

	// Picker options
	public List<string> ScanTypeOptions { get; } = new() { "ENTRY", "EXIT" };

	public SetupViewModel(
		ISecureConfigService secureConfig,
		IPreferencesService preferences,
		FileConfigService fileConfig,
		INavigationService navigation,
		IConnectionTestService connectionTestService,
		IDeviceDetectionService deviceDetection,
		ILogger<SetupViewModel> logger)
	{
		_secureConfig = secureConfig;
		_preferences = preferences;
		_fileConfig = fileConfig;
		_navigation = navigation;
		_connectionTestService = connectionTestService;
		_deviceDetection = deviceDetection;
		_logger = logger;
	}

	/// <summary>
	/// Initialize page - load existing config if available and detect devices.
	/// </summary>
	public async Task InitializeAsync()
	{
		// Load existing configuration if available
		try
		{
			var existingConfig = await _fileConfig.LoadConfigAsync();
			if (existingConfig.SetupCompleted)
			{
				// Edit mode - pre-fill form with existing values
				IsEditMode = true;
				PageTitle = "Edit Configuration";
				SaveButtonText = "Save Changes";

				ServerUrl = existingConfig.ServerUrl;
				ApiKey = existingConfig.ApiKey;
				HmacSecret = existingConfig.HmacSecret;
				SelectedScanType = existingConfig.DefaultScanType;
				AcceptSelfSignedCerts = existingConfig.AcceptSelfSignedCerts;

				_logger.LogInformation("Loaded existing configuration for editing");
			}
			else
			{
				// Initial setup mode
				IsEditMode = false;
				PageTitle = "Device Configuration";
				SaveButtonText = "Complete Setup";
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not load existing configuration, starting fresh");
			IsEditMode = false;
			PageTitle = "Device Configuration";
			SaveButtonText = "Complete Setup";
		}

		// Detect devices
		IsDetectingDevices = true;
		DetectedDevicesMessage = "Detecting available cameras and USB scanners...";

		try
		{
			var method = await _deviceDetection.DetectDevicesAsync();
			DetectedScanMethod = method;
			DetectedDevicesMessage = _deviceDetection.GetDetectionSummary();

			_logger.LogInformation("Device detection complete: {Method}", method);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Device detection failed");
			DetectedScanMethod = ScanningMethod.UsbScanner; // Fallback to USB
			DetectedDevicesMessage = "Device detection failed. Defaulting to USB scanner mode.";
		}
		finally
		{
			IsDetectingDevices = false;
		}
	}

	[RelayCommand]
	private async Task SaveAsync()
	{
		// Clear previous error
		SaveError = null;

		// Validate all fields
		if (!ValidateAll())
		{
			return; // Stay on page, errors shown inline
		}

		IsSaving = true;

		try
		{
			// Save configuration using file-based storage (more reliable)
			var config = new AppConfig
			{
				ServerUrl = ServerUrl,
				ApiKey = ApiKey,
				HmacSecret = HmacSecret,
				ScanMode = DetectedScanMethod switch
				{
					ScanningMethod.Camera => "Camera",
					ScanningMethod.CameraWithUsbFallback => "Camera",
					ScanningMethod.UsbScanner => "USB",
					_ => "USB"
				},
				DefaultScanType = SelectedScanType,
				SetupCompleted = true,
				SoundEnabled = true,
				AcceptSelfSignedCerts = AcceptSelfSignedCerts
			};

			await _fileConfig.SaveConfigAsync(config);
			_logger.LogInformation("Configuration saved successfully");

			// Also try to save to secure storage (fallback to preferences if unavailable)
			try
			{
				await _secureConfig.SetApiKeyAsync(ApiKey);
				if (!string.IsNullOrWhiteSpace(HmacSecret))
				{
					await _secureConfig.SetHmacSecretAsync(HmacSecret);
				}
			}
			catch (Exception secEx)
			{
				_logger.LogWarning(secEx, "Could not save to secure storage, config.json will be used instead");
			}

			// Navigate to main page
			await _navigation.GoToAsync("//main");
		}
		catch (Exception ex)
		{
			// AC7: Show error, don't set Setup.Completed, preserve values
			SaveError = $"Failed to save configuration: {ex.Message}. Please try again.";
			_logger.LogError(ex, "Setup save failed");
		}
		finally
		{
			IsSaving = false;
		}
	}

	private bool ValidateAll()
	{
		bool isValid = true;

		// AC3: Validate Server URL
		if (string.IsNullOrWhiteSpace(ServerUrl))
		{
			ServerUrlError = "This field is required";
			isValid = false;
		}
		else if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ||
		         (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			ServerUrlError = "Please enter a valid URL (e.g., https://192.168.1.100:8443)";
			isValid = false;
		}
		else
		{
			ServerUrlError = null;
		}

		// AC4: Validate API Key (required)
		if (string.IsNullOrWhiteSpace(ApiKey))
		{
			ApiKeyError = "This field is required";
			isValid = false;
		}
		else
		{
			ApiKeyError = null;
		}

		// HMAC Secret is optional (v1.0 - server doesn't provide it yet)
		HmacSecretError = null;

		return isValid;
	}

	// US0005: Test Connection command
	[RelayCommand(CanExecute = nameof(CanTestConnection))]
	private async Task TestConnectionAsync()
	{
		IsTestingConnection = true;
		TestResult = ConnectionTestResult.None;
		TestResultMessage = null;

		try
		{
			var result = await _connectionTestService.TestConnectionAsync(ServerUrl, ApiKey);
			TestResult = result.Status;
			TestResultMessage = result.Message;
			IsConnectionValid = result.Status == ConnectionTestResult.Success;
		}
		finally
		{
			IsTestingConnection = false;
		}
	}

	private bool CanTestConnection() =>
		!IsTestingConnection &&
		!string.IsNullOrWhiteSpace(ServerUrl) &&
		!string.IsNullOrWhiteSpace(ApiKey);

	// US0005: Clear test results when URL or API Key changes
	partial void OnServerUrlChanged(string value)
	{
		TestResult = ConnectionTestResult.None;
		TestResultMessage = null;
		IsConnectionValid = false;
		TestConnectionCommand.NotifyCanExecuteChanged();
	}

	partial void OnApiKeyChanged(string value)
	{
		TestResult = ConnectionTestResult.None;
		TestResultMessage = null;
		IsConnectionValid = false;
		TestConnectionCommand.NotifyCanExecuteChanged();
	}

	// Cancel command - return to main page without saving
	[RelayCommand]
	private async Task CancelAsync()
	{
		await _navigation.GoToAsync("//main");
	}
}
