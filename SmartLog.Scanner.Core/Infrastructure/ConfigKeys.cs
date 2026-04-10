namespace SmartLog.Scanner.Core.Infrastructure;

/// <summary>
/// Centralized storage key constants to avoid magic strings throughout the codebase.
/// Used by SecureConfigService (encrypted credentials) and PreferencesService (non-sensitive settings).
/// </summary>
public static class ConfigKeys
{
    #region Secure Storage Keys (encrypted via MAUI SecureStorage - Keychain on macOS, DPAPI on Windows)

    /// <summary>
    /// Storage key for API key (X-API-Key header value for SmartLog Admin Web App authentication)
    /// </summary>
    public const string ApiKey = "Server.ApiKey";

    /// <summary>
    /// Storage key for HMAC-SHA256 shared secret (used to validate QR code signatures)
    /// </summary>
    public const string HmacSecretKey = "Security.HmacSecretKey";

    #endregion

    #region Preferences Keys (plain text via MAUI Preferences)

    /// <summary>
    /// Server base URL (e.g., "https://192.168.1.100:8443")
    /// Default: "" (empty string)
    /// </summary>
    public const string ServerBaseUrl = "Server.BaseUrl";

    /// <summary>
    /// Scan input mode: "Camera" or "USB" (keyboard wedge)
    /// Default: "USB"
    /// </summary>
    public const string ScanMode = "Scanner.Mode";

    /// <summary>
    /// Default scan direction: "ENTRY" or "EXIT"
    /// Default: "ENTRY"
    /// </summary>
    public const string DefaultScanType = "Scanner.DefaultScanType";

    /// <summary>
    /// Audio feedback enabled toggle
    /// Default: true
    /// </summary>
    public const string SoundEnabled = "Scanner.SoundEnabled";

    /// <summary>
    /// First-launch setup completion flag (guards navigation to main screen)
    /// Default: false
    /// </summary>
    public const string SetupCompleted = "Setup.Completed";

    /// <summary>
    /// Unique device identifier (e.g., "SCANNER-MACBOOK-A1B2C3D4")
    /// Default: "" (generated on first setup)
    /// </summary>
    public const string DeviceId = "Device.Id";

    /// <summary>
    /// Human-readable device name (e.g., "Scanner-MacBook")
    /// Default: "" (generated on first setup)
    /// </summary>
    public const string DeviceName = "Device.Name";

    /// <summary>
    /// Accept self-signed TLS certificates for LAN deployments
    /// Default: false (production-safe)
    /// </summary>
    public const string AcceptSelfSignedCerts = "Security.AcceptSelfSignedCerts";

    /// <summary>
    /// Device ID of the selected camera (platform-specific opaque string)
    /// Default: "" (system picks default camera)
    /// </summary>
    public const string SelectedCameraId = "Scanner.SelectedCameraId";

    #endregion
}
