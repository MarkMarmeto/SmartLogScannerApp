using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// Unit tests for IPreferencesService contract using an in-memory implementation.
/// The concrete PreferencesService wraps MAUI Preferences (static API) which is
/// unavailable in unit tests — these tests validate the interface behavior.
/// Test Spec: TS0001 (TC016-TC026)
/// </summary>
public class PreferencesServiceTests
{
    /// <summary>
    /// In-memory implementation of IPreferencesService for unit testing.
    /// Mirrors the default values and behavior of the real PreferencesService.
    /// </summary>
    private class InMemoryPreferencesService : IPreferencesService
    {
        private readonly Dictionary<string, object> _store = new();

        public string GetServerBaseUrl() => Get("ServerBaseUrl", string.Empty);
        public void SetServerBaseUrl(string url) => _store["ServerBaseUrl"] = url;

        public string GetScanMode() => Get("ScanMode", "USB");
        public void SetScanMode(string mode) => _store["ScanMode"] = mode;

        public string GetDefaultScanType() => Get("DefaultScanType", "ENTRY");
        public void SetDefaultScanType(string scanType) => _store["DefaultScanType"] = scanType;

        public bool GetSoundEnabled() => Get("SoundEnabled", true);
        public void SetSoundEnabled(bool enabled) => _store["SoundEnabled"] = enabled;

        public bool GetSetupCompleted() => Get("SetupCompleted", false);
        public void SetSetupCompleted(bool completed) => _store["SetupCompleted"] = completed;

        public string GetDeviceId() => Get("DeviceId", string.Empty);
        public void SetDeviceId(string deviceId) => _store["DeviceId"] = deviceId;

        public string GetDeviceName() => Get("DeviceName", string.Empty);
        public void SetDeviceName(string deviceName) => _store["DeviceName"] = deviceName;

        public bool GetAcceptSelfSignedCerts() => Get("AcceptSelfSignedCerts", false);
        public void SetAcceptSelfSignedCerts(bool accept) => _store["AcceptSelfSignedCerts"] = accept;

        public void ClearAll() => _store.Clear();

        private T Get<T>(string key, T defaultValue)
            => _store.TryGetValue(key, out var value) ? (T)value : defaultValue;
    }

    private IPreferencesService CreateService() => new InMemoryPreferencesService();

    #region TC016-TC017: ServerBaseUrl

    [Fact]
    public void SetServerBaseUrl_StoresAndRetrieves_Successfully()
    {
        var service = CreateService();
        const string testUrl = "https://192.168.1.100:8443";

        service.SetServerBaseUrl(testUrl);
        var retrieved = service.GetServerBaseUrl();

        Assert.Equal(testUrl, retrieved);
    }

    [Fact]
    public void GetServerBaseUrl_WhenNotSet_ReturnsEmptyStringDefault()
    {
        var service = CreateService();

        var result = service.GetServerBaseUrl();

        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region TC018-TC019: ScanMode

    [Fact]
    public void SetScanMode_Camera_StoresAndRetrievesCamera()
    {
        var service = CreateService();
        const string testMode = "Camera";

        service.SetScanMode(testMode);
        var retrieved = service.GetScanMode();

        Assert.Equal(testMode, retrieved);
    }

    [Fact]
    public void GetScanMode_WhenNotSet_ReturnsUSBDefault()
    {
        var service = CreateService();

        var result = service.GetScanMode();

        Assert.Equal("USB", result);
    }

    #endregion

    #region TC020-TC021: DefaultScanType

    [Fact]
    public void SetDefaultScanType_EXIT_StoresAndRetrievesEXIT()
    {
        var service = CreateService();
        const string testType = "EXIT";

        service.SetDefaultScanType(testType);
        var retrieved = service.GetDefaultScanType();

        Assert.Equal(testType, retrieved);
    }

    [Fact]
    public void GetDefaultScanType_WhenNotSet_ReturnsENTRYDefault()
    {
        var service = CreateService();

        var result = service.GetDefaultScanType();

        Assert.Equal("ENTRY", result);
    }

    #endregion

    #region TC022-TC023: SoundEnabled

    [Fact]
    public void SetSoundEnabled_False_StoresAndRetrievesFalse()
    {
        var service = CreateService();
        const bool testValue = false;

        service.SetSoundEnabled(testValue);
        var retrieved = service.GetSoundEnabled();

        Assert.Equal(testValue, retrieved);
    }

    [Fact]
    public void GetSoundEnabled_WhenNotSet_ReturnsTrueDefault()
    {
        var service = CreateService();

        var result = service.GetSoundEnabled();

        Assert.True(result);
    }

    #endregion

    #region TC024-TC025: SetupCompleted

    [Fact]
    public void SetSetupCompleted_True_StoresAndRetrievesTrue()
    {
        var service = CreateService();
        const bool testValue = true;

        service.SetSetupCompleted(testValue);
        var retrieved = service.GetSetupCompleted();

        Assert.Equal(testValue, retrieved);
    }

    [Fact]
    public void GetSetupCompleted_WhenNotSet_ReturnsFalseDefault()
    {
        var service = CreateService();

        var result = service.GetSetupCompleted();

        Assert.False(result);
    }

    #endregion

    #region TC026: ClearAll

    [Fact]
    public void ClearAll_RemovesAllPreferences()
    {
        var service = CreateService();

        service.SetServerBaseUrl("https://test.com");
        service.SetScanMode("Camera");
        service.SetDefaultScanType("EXIT");
        service.SetSoundEnabled(false);
        service.SetSetupCompleted(true);

        service.ClearAll();

        Assert.Equal(string.Empty, service.GetServerBaseUrl());
        Assert.Equal("USB", service.GetScanMode());
        Assert.Equal("ENTRY", service.GetDefaultScanType());
        Assert.True(service.GetSoundEnabled());
        Assert.False(service.GetSetupCompleted());
    }

    #endregion
}
