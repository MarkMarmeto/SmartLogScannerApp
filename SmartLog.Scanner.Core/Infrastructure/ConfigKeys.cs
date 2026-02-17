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

    #endregion
}
