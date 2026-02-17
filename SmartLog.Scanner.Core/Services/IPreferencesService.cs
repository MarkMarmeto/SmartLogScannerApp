namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Service for non-sensitive application settings via MAUI Preferences
/// Stores server URL, scan mode, scan type, sound toggle, and setup completion flag.
/// </summary>
public interface IPreferencesService
{
    #region Server Base URL

    /// <summary>
    /// Gets the SmartLog Admin Web App server base URL
    /// </summary>
    /// <returns>Server URL, or empty string if not set</returns>
    string GetServerBaseUrl();

    /// <summary>
    /// Sets the SmartLog Admin Web App server base URL
    /// </summary>
    /// <param name="url">Server URL (e.g., "https://192.168.1.100:8443")</param>
    void SetServerBaseUrl(string url);

    #endregion

    #region Scan Mode

    /// <summary>
    /// Gets the scan input mode
    /// </summary>
    /// <returns>Scan mode: "Camera" or "USB", default "USB"</returns>
    string GetScanMode();

    /// <summary>
    /// Sets the scan input mode
    /// </summary>
    /// <param name="mode">Scan mode: "Camera" or "USB"</param>
    void SetScanMode(string mode);

    #endregion

    #region Default Scan Type

    /// <summary>
    /// Gets the default scan direction
    /// </summary>
    /// <returns>Scan type: "ENTRY" or "EXIT", default "ENTRY"</returns>
    string GetDefaultScanType();

    /// <summary>
    /// Sets the default scan direction
    /// </summary>
    /// <param name="scanType">Scan type: "ENTRY" or "EXIT"</param>
    void SetDefaultScanType(string scanType);

    #endregion

    #region Sound Enabled

    /// <summary>
    /// Gets whether audio feedback is enabled
    /// </summary>
    /// <returns>True if sound enabled, default true</returns>
    bool GetSoundEnabled();

    /// <summary>
    /// Sets whether audio feedback is enabled
    /// </summary>
    /// <param name="enabled">True to enable sound</param>
    void SetSoundEnabled(bool enabled);

    #endregion

    #region Setup Completed

    /// <summary>
    /// Gets whether the first-launch setup wizard has been completed
    /// </summary>
    /// <returns>True if setup completed, default false</returns>
    bool GetSetupCompleted();

    /// <summary>
    /// Sets whether the first-launch setup wizard has been completed
    /// </summary>
    /// <param name="completed">True if setup completed</param>
    void SetSetupCompleted(bool completed);

    #endregion

    #region Clear All

    /// <summary>
    /// Clears all stored preferences (resets to defaults)
    /// </summary>
    void ClearAll();

    #endregion
}
