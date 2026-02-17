using SmartLog.Scanner.Core.Infrastructure;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// Unit tests for PreferencesService (wrapping MAUI Preferences for non-sensitive settings)
/// Tests validate AC3 and AC7 from US0001
/// Test Spec: TS0001 (TC016-TC026)
/// </summary>
public class PreferencesServiceTests
{
    #region TC016-TC017: ServerBaseUrl

    [Fact]
    public void SetServerBaseUrl_StoresAndRetrieves_Successfully()
    {
        // Arrange: TC016 - Store and retrieve server URL
        var service = new PreferencesService();
        const string testUrl = "https://192.168.1.100:8443";

        // Act
        service.SetServerBaseUrl(testUrl);
        var retrieved = service.GetServerBaseUrl();

        // Assert
        Assert.Equal(testUrl, retrieved);
    }

    [Fact]
    public void GetServerBaseUrl_WhenNotSet_ReturnsEmptyStringDefault()
    {
        // Arrange: TC017 - AC7: Get ServerBaseUrl when never set should return "" (default)
        var service = new PreferencesService();

        // Clear any existing value to ensure clean state
        service.ClearAll();

        // Act
        var result = service.GetServerBaseUrl();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region TC018-TC019: ScanMode

    [Fact]
    public void SetScanMode_Camera_StoresAndRetrievesCamera()
    {
        // Arrange: TC018 - Store and retrieve scan mode "Camera"
        var service = new PreferencesService();
        const string testMode = "Camera";

        // Act
        service.SetScanMode(testMode);
        var retrieved = service.GetScanMode();

        // Assert
        Assert.Equal(testMode, retrieved);
    }

    [Fact]
    public void GetScanMode_WhenNotSet_ReturnsUSBDefault()
    {
        // Arrange: TC019 - AC7: Get ScanMode when never set should return "USB" (default)
        var service = new PreferencesService();

        // Clear any existing value to ensure clean state
        service.ClearAll();

        // Act
        var result = service.GetScanMode();

        // Assert
        Assert.Equal("USB", result);
    }

    #endregion

    #region TC020-TC021: DefaultScanType

    [Fact]
    public void SetDefaultScanType_EXIT_StoresAndRetrievesEXIT()
    {
        // Arrange: TC020 - Store and retrieve scan type "EXIT"
        var service = new PreferencesService();
        const string testType = "EXIT";

        // Act
        service.SetDefaultScanType(testType);
        var retrieved = service.GetDefaultScanType();

        // Assert
        Assert.Equal(testType, retrieved);
    }

    [Fact]
    public void GetDefaultScanType_WhenNotSet_ReturnsENTRYDefault()
    {
        // Arrange: TC021 - AC7: Get DefaultScanType when never set should return "ENTRY" (default)
        var service = new PreferencesService();

        // Clear any existing value to ensure clean state
        service.ClearAll();

        // Act
        var result = service.GetDefaultScanType();

        // Assert
        Assert.Equal("ENTRY", result);
    }

    #endregion

    #region TC022-TC023: SoundEnabled

    [Fact]
    public void SetSoundEnabled_False_StoresAndRetrievesFalse()
    {
        // Arrange: TC022 - Store and retrieve sound enabled = false
        var service = new PreferencesService();
        const bool testValue = false;

        // Act
        service.SetSoundEnabled(testValue);
        var retrieved = service.GetSoundEnabled();

        // Assert
        Assert.Equal(testValue, retrieved);
    }

    [Fact]
    public void GetSoundEnabled_WhenNotSet_ReturnsTrueDefault()
    {
        // Arrange: TC023 - AC7: Get SoundEnabled when never set should return true (default)
        var service = new PreferencesService();

        // Clear any existing value to ensure clean state
        service.ClearAll();

        // Act
        var result = service.GetSoundEnabled();

        // Assert
        Assert.True(result);
    }

    #endregion

    #region TC024-TC025: SetupCompleted

    [Fact]
    public void SetSetupCompleted_True_StoresAndRetrievesTrue()
    {
        // Arrange: TC024 - Store and retrieve setup completed = true
        var service = new PreferencesService();
        const bool testValue = true;

        // Act
        service.SetSetupCompleted(testValue);
        var retrieved = service.GetSetupCompleted();

        // Assert
        Assert.Equal(testValue, retrieved);
    }

    [Fact]
    public void GetSetupCompleted_WhenNotSet_ReturnsFalseDefault()
    {
        // Arrange: TC025 - AC7: Get SetupCompleted when never set should return false (default)
        var service = new PreferencesService();

        // Clear any existing value to ensure clean state
        service.ClearAll();

        // Act
        var result = service.GetSetupCompleted();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region TC026: ClearAll

    [Fact]
    public void ClearAll_RemovesAllPreferences()
    {
        // Arrange: TC026 - Clear all preferences and verify defaults are returned
        var service = new PreferencesService();

        // Set all preferences to non-default values
        service.SetServerBaseUrl("https://test.com");
        service.SetScanMode("Camera");
        service.SetDefaultScanType("EXIT");
        service.SetSoundEnabled(false);
        service.SetSetupCompleted(true);

        // Act
        service.ClearAll();

        // Assert - All values should return to defaults
        Assert.Equal(string.Empty, service.GetServerBaseUrl());
        Assert.Equal("USB", service.GetScanMode());
        Assert.Equal("ENTRY", service.GetDefaultScanType());
        Assert.True(service.GetSoundEnabled());
        Assert.False(service.GetSetupCompleted());
    }

    #endregion
}
