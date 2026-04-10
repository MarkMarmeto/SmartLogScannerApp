using Microsoft.Maui.Storage;
using SmartLog.Scanner.Core.Infrastructure;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Implementation of IPreferencesService wrapping MAUI Preferences
/// Stores non-sensitive application settings with default values per AC7
/// </summary>
public class PreferencesService : IPreferencesService
{
    #region Server Base URL

    public string GetServerBaseUrl()
    {
        // AC7: Return empty string as default when not set
        return Preferences.Default.Get(ConfigKeys.ServerBaseUrl, string.Empty);
    }

    public void SetServerBaseUrl(string url)
    {
        Preferences.Default.Set(ConfigKeys.ServerBaseUrl, url);
    }

    #endregion

    #region Scan Mode

    public string GetScanMode()
    {
        // AC7: Return "USB" as default when not set
        return Preferences.Default.Get(ConfigKeys.ScanMode, "USB");
    }

    public void SetScanMode(string mode)
    {
        Preferences.Default.Set(ConfigKeys.ScanMode, mode);
    }

    #endregion

    #region Default Scan Type

    public string GetDefaultScanType()
    {
        // AC7: Return "ENTRY" as default when not set
        return Preferences.Default.Get(ConfigKeys.DefaultScanType, "ENTRY");
    }

    public void SetDefaultScanType(string scanType)
    {
        Preferences.Default.Set(ConfigKeys.DefaultScanType, scanType);
    }

    #endregion

    #region Sound Enabled

    public bool GetSoundEnabled()
    {
        // AC7: Return true as default when not set
        return Preferences.Default.Get(ConfigKeys.SoundEnabled, true);
    }

    public void SetSoundEnabled(bool enabled)
    {
        Preferences.Default.Set(ConfigKeys.SoundEnabled, enabled);
    }

    #endregion

    #region Setup Completed

    public bool GetSetupCompleted()
    {
        // AC7: Return false as default when not set
        return Preferences.Default.Get(ConfigKeys.SetupCompleted, false);
    }

    public void SetSetupCompleted(bool completed)
    {
        Preferences.Default.Set(ConfigKeys.SetupCompleted, completed);
    }

    #endregion

    #region Device Identity

    public string GetDeviceId()
    {
        return Preferences.Default.Get(ConfigKeys.DeviceId, string.Empty);
    }

    public void SetDeviceId(string deviceId)
    {
        Preferences.Default.Set(ConfigKeys.DeviceId, deviceId);
    }

    public string GetDeviceName()
    {
        return Preferences.Default.Get(ConfigKeys.DeviceName, string.Empty);
    }

    public void SetDeviceName(string deviceName)
    {
        Preferences.Default.Set(ConfigKeys.DeviceName, deviceName);
    }

    #endregion

    #region Accept Self-Signed Certs

    public bool GetAcceptSelfSignedCerts()
    {
        return Preferences.Default.Get(ConfigKeys.AcceptSelfSignedCerts, false);
    }

    public void SetAcceptSelfSignedCerts(bool accept)
    {
        Preferences.Default.Set(ConfigKeys.AcceptSelfSignedCerts, accept);
    }

    #endregion

    #region Selected Camera

    public string GetSelectedCameraId()
    {
        return Preferences.Default.Get(ConfigKeys.SelectedCameraId, string.Empty);
    }

    public void SetSelectedCameraId(string deviceId)
    {
        Preferences.Default.Set(ConfigKeys.SelectedCameraId, deviceId);
    }

    #endregion

    #region Clear All

    public void ClearAll()
    {
        // AC3: Clear all stored preferences (resets to defaults)
        Preferences.Default.Clear();
    }

    #endregion
}
