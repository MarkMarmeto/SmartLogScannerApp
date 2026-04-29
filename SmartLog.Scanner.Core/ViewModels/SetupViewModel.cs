using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace SmartLog.Scanner.Core.ViewModels;

/// <summary>
/// US0004/US0005: ViewModel for device setup wizard page.
/// Handles form validation, connection testing, credential storage, and navigation.
/// </summary>
public partial class SetupViewModel : ObservableObject
{
	private readonly ISecureConfigService _secureConfig;
	private readonly IPreferencesService _preferences;
	private readonly INavigationService _navigation;
	private readonly IConnectionTestService _connectionTestService;
	private readonly IDeviceDetectionService _deviceDetection;
	private readonly ICameraEnumerationService? _cameraEnumeration;
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

	// EP0012 (US0122): Concurrent scanner mode opt-in
	[ObservableProperty] private bool _enableUsbScannerInput;

	/// <summary>True when a camera is detected — concurrent USB requires a camera pipeline.</summary>
	public bool CanEnableUsb =>
		DetectedScanMethod is ScanningMethod.Camera or ScanningMethod.CameraWithUsbFallback;

	// Camera picker
	[ObservableProperty] private ObservableCollection<CameraDeviceInfo> _availableCameras = new();
	[ObservableProperty] private CameraDeviceInfo? _selectedCamera;
	[ObservableProperty] private bool _hasCameras;
	[ObservableProperty] private string _cameraPickerMessage = "Detecting cameras...";


	// Picker options
	public List<string> ScanTypeOptions { get; } = new() { "ENTRY", "EXIT" };

	public SetupViewModel(
		ISecureConfigService secureConfig,
		IPreferencesService preferences,
		INavigationService navigation,
		IConnectionTestService connectionTestService,
		IDeviceDetectionService deviceDetection,
		ILogger<SetupViewModel> logger,
		ICameraEnumerationService? cameraEnumeration = null)
	{
		_secureConfig = secureConfig;
		_preferences = preferences;
		_navigation = navigation;
		_connectionTestService = connectionTestService;
		_deviceDetection = deviceDetection;
		_cameraEnumeration = cameraEnumeration;
		_logger = logger;
	}

	/// <summary>
	/// Initialize page - load existing config if available and detect devices.
	/// </summary>
	public async Task InitializeAsync()
	{
		// Load existing configuration from preferences
		if (_preferences.GetSetupCompleted())
		{
			// Edit mode - pre-fill form with existing values
			IsEditMode = true;
			PageTitle = "Edit Configuration";
			SaveButtonText = "Save Changes";

			ServerUrl = _preferences.GetServerBaseUrl();

			// Load secrets from SecureStorage ONLY
			ApiKey = await _secureConfig.GetApiKeyAsync() ?? string.Empty;
			HmacSecret = await _secureConfig.GetHmacSecretAsync() ?? string.Empty;

			SelectedScanType = _preferences.GetDefaultScanType();
			AcceptSelfSignedCerts = _preferences.GetAcceptSelfSignedCerts();
			EnableUsbScannerInput = _preferences.GetScanMode() == "Both";

			_logger.LogInformation("Loaded existing configuration for editing");
		}
		else
		{
			// Initial setup mode
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
			DetectedScanMethod = ScanningMethod.UsbScanner;
			DetectedDevicesMessage = "Device detection failed. Defaulting to USB scanner mode.";
		}
		finally
		{
			IsDetectingDevices = false;
		}

		// Enumerate cameras for the camera picker
		await LoadCamerasAsync();
	}

	private async Task LoadCamerasAsync()
	{
		if (_cameraEnumeration == null)
		{
			HasCameras = false;
			CameraPickerMessage = "Camera enumeration not available on this platform.";
			return;
		}

		try
		{
			var cameras = await _cameraEnumeration.GetAvailableCamerasAsync();
			AvailableCameras = new ObservableCollection<CameraDeviceInfo>(cameras);
			HasCameras = cameras.Count > 0;

			if (cameras.Count == 0)
			{
				CameraPickerMessage = "No cameras detected. USB scanner will be used.";
				return;
			}

			// Single-camera picker (Scanner Configuration section — backward compat)
			var savedId = _preferences.GetSelectedCameraId();
			SelectedCamera = cameras.FirstOrDefault(c => c.Id == savedId) ?? cameras[0];

			var slotCount = Math.Min(cameras.Count, 3);
			CameraPickerMessage = cameras.Count > 3
				? $"3 of {cameras.Count} camera(s) detected (max 3)"
				: $"{cameras.Count} camera(s) detected";

			CameraSlots.Clear();
			for (var i = 0; i < slotCount; i++)
			{
				var device = cameras[i];
				var savedName = _preferences.GetCameraName(i);
				var autoName = $"Camera {i + 1} – {device.Name}";  // en-dash

				var slot = new CameraSlotViewModel(i, _cameraEnumeration,
					Microsoft.Extensions.Logging.Abstractions.NullLogger<CameraSlotViewModel>.Instance)
				{
					SelectedDevice = device,
					IsEnabled      = _preferences.GetCameraEnabled(i),
					DisplayName    = string.IsNullOrWhiteSpace(savedName) ? autoName : savedName,
				};
				CameraSlots.Add(slot);
			}

			_logger.LogInformation("Auto-created {Count} camera slot(s)", slotCount);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Camera enumeration failed");
			CameraPickerMessage = "Could not enumerate cameras.";
			HasCameras = false;
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
			// SECURITY FIX (CRITICAL-01): Save secrets to SecureStorage FIRST
			// This ensures secrets are stored securely before anything else
			try
			{
				await _secureConfig.SetApiKeyAsync(ApiKey);
				if (!string.IsNullOrWhiteSpace(HmacSecret))
				{
					await _secureConfig.SetHmacSecretAsync(HmacSecret);
				}
				_logger.LogInformation("Secrets saved to SecureStorage");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to save secrets to SecureStorage");
				SaveError = "Cannot securely store credentials. Device may not support SecureStorage.";
				return;
			}

			// Save non-sensitive configuration to preferences
			_preferences.SetServerBaseUrl(ServerUrl);
			_preferences.SetDeviceId(GenerateDeviceId());
			_preferences.SetDeviceName($"Scanner-{Environment.MachineName}");
			_preferences.SetScanMode(ResolveScanMode(DetectedScanMethod, EnableUsbScannerInput));
			_preferences.SetDefaultScanType(SelectedScanType);
			_preferences.SetAcceptSelfSignedCerts(AcceptSelfSignedCerts);
			_preferences.SetSelectedCameraId(SelectedCamera?.Id ?? string.Empty);

			// EP0011: Save multi-camera config
			SaveMultiCameraConfig();

			_preferences.SetSetupCompleted(true);
			_logger.LogInformation("Configuration saved to preferences");

			// Navigate to main page
			_logger.LogInformation("Navigating to //main");
			await _navigation.GoToAsync("//main");
			_logger.LogInformation("Navigation to //main returned");
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

		// Validate Server URL
		if (string.IsNullOrWhiteSpace(ServerUrl))
		{
			ServerUrlError = "This field is required";
			isValid = false;
		}
		else if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ||
		         (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			ServerUrlError = "Please enter a valid URL (e.g., http://192.168.1.100:8080)";
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
			var result = await _connectionTestService.TestConnectionAsync(ServerUrl, ApiKey, AcceptSelfSignedCerts);
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

	// EP0012 (US0122): Refresh CanEnableUsb and update banner when detection result arrives.
	partial void OnDetectedScanMethodChanged(ScanningMethod value)
	{
		OnPropertyChanged(nameof(CanEnableUsb));
		if (value == ScanningMethod.CameraWithUsbFallback)
			DetectedDevicesMessage = "Detected: webcam + USB scanner. Tick the option below to use both at the same time.";
	}

	/// <summary>
	/// EP0012/US0122 AC4: Resolves the Scanner.Mode string from detection result and opt-in checkbox.
	/// </summary>
	internal static string ResolveScanMode(ScanningMethod detected, bool enableUsb) => detected switch
	{
		ScanningMethod.Camera or ScanningMethod.CameraWithUsbFallback when enableUsb => "Both",
		ScanningMethod.Camera or ScanningMethod.CameraWithUsbFallback => "Camera",
		ScanningMethod.UsbScanner => "USB",
		_ => "USB"
	};

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

	// ── EP0011/US0127: Multi-Camera Config ──────────────────────────────────

	public ObservableCollection<CameraSlotViewModel> CameraSlots { get; } = new();

	private void SaveMultiCameraConfig()
	{
		_preferences.SetCameraCount(CameraSlots.Count);
		for (var i = 0; i < CameraSlots.Count; i++)
		{
			var slot = CameraSlots[i];
			_preferences.SetCameraName(i, slot.DisplayName);
			_preferences.SetCameraDeviceId(i, slot.SelectedDevice?.Id ?? string.Empty);
			_preferences.SetCameraEnabled(i, slot.IsEnabled);
		}
	}

	/// <summary>
	/// Generates a unique device ID based on machine name and a GUID.
	/// </summary>
	private string GenerateDeviceId()
	{
		// Reuse existing DeviceId if available
		var existingId = _preferences.GetDeviceId();
		if (!string.IsNullOrWhiteSpace(existingId))
		{
			return existingId;
		}

		// Generate new ID
		var machineName = Environment.MachineName.Replace(" ", "-").ToUpperInvariant();
		var shortGuid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		return $"SCANNER-{machineName}-{shortGuid}";
	}
}
