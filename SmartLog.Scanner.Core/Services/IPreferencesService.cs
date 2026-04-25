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

    #region Device Identity

    /// <summary>
    /// Gets the unique device identifier
    /// </summary>
    string GetDeviceId();

    /// <summary>
    /// Sets the unique device identifier
    /// </summary>
    void SetDeviceId(string deviceId);

    /// <summary>
    /// Gets the human-readable device name
    /// </summary>
    string GetDeviceName();

    /// <summary>
    /// Sets the human-readable device name
    /// </summary>
    void SetDeviceName(string deviceName);

    #endregion

    #region Accept Self-Signed Certs

    /// <summary>
    /// Gets whether self-signed TLS certificates are accepted
    /// </summary>
    bool GetAcceptSelfSignedCerts();

    /// <summary>
    /// Sets whether self-signed TLS certificates are accepted
    /// </summary>
    void SetAcceptSelfSignedCerts(bool accept);

    #endregion

    #region Selected Camera

    /// <summary>
    /// Gets the platform-specific device ID of the selected camera.
    /// Empty string means "use system default".
    /// </summary>
    string GetSelectedCameraId();

    /// <summary>
    /// Sets the selected camera device ID.
    /// </summary>
    void SetSelectedCameraId(string deviceId);

    #endregion

    #region Clear All

    /// <summary>
    /// Clears all stored preferences (resets to defaults)
    /// </summary>
    void ClearAll();

    #endregion

    #region Multi-Camera Config (EP0011)

    /// <summary>Gets the configured number of cameras (1–8). Default: 1.</summary>
    int GetCameraCount();

    /// <summary>Sets the configured number of cameras (1–8).</summary>
    void SetCameraCount(int count);

    /// <summary>Gets the display name for camera at index. Default: "Camera {index+1}".</summary>
    string GetCameraName(int index);

    /// <summary>Sets the display name for camera at index.</summary>
    void SetCameraName(int index, string name);

    /// <summary>Gets the device ID assigned to camera at index. Default: "".</summary>
    string GetCameraDeviceId(int index);

    /// <summary>Sets the device ID for camera at index.</summary>
    void SetCameraDeviceId(int index, string deviceId);

    /// <summary>Gets the scan type for camera at index. Default: "ENTRY".</summary>
    string GetCameraScanType(int index);

    /// <summary>Sets the scan type for camera at index ("ENTRY" or "EXIT").</summary>
    void SetCameraScanType(int index, string scanType);

    /// <summary>Gets whether camera at index is enabled. Default: true.</summary>
    bool GetCameraEnabled(int index);

    /// <summary>Sets whether camera at index is enabled.</summary>
    void SetCameraEnabled(int index, bool enabled);

    #endregion
}
