using Microsoft.Extensions.Logging;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using SmartLog.Scanner.Core.Exceptions;
using SmartLog.Scanner.Core.Infrastructure;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Implementation of ISecureConfigService wrapping MAUI SecureStorage
/// Stores API key and HMAC secret encrypted via platform-native mechanisms:
/// - macOS: Keychain
/// - Windows: DPAPI (Data Protection API)
/// </summary>
public class SecureConfigService : ISecureConfigService
{
    private readonly ILogger<SecureConfigService> _logger;
    private readonly string _platform;

    public SecureConfigService(ILogger<SecureConfigService> logger)
    {
        _logger = logger;

        // Detect platform at runtime for better reliability
        _platform = DeviceInfo.Current.Platform.ToString();
    }

    /// <summary>
    /// Whether to fall back to <see cref="Preferences"/> when <see cref="SecureStorage"/> is
    /// unavailable. DEBUG builds always allow it; Release allows it on Windows only because
    /// unpackaged MAUI apps frequently fail SecureStorage at first use (DPAPI quirks under
    /// roaming profiles, AppContainer restrictions, etc.). macOS Release still fails fast.
    /// </summary>
    private static bool ShouldFallBackToPreferences()
    {
#if DEBUG
        return true;
#else
        return DeviceInfo.Current.Platform == DevicePlatform.WinUI;
#endif
    }

    #region API Key

    public async Task<string?> GetApiKeyAsync()
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(ConfigKeys.ApiKey);

            // If SecureStorage returned null, check Preferences fallback (the key may have been
            // stored there if SecureStorage threw during a previous Set on this platform).
            if (value == null && ShouldFallBackToPreferences())
            {
                var fallback = Preferences.Default.Get<string>(ConfigKeys.ApiKey, string.Empty);
                if (!string.IsNullOrEmpty(fallback))
                {
                    _logger.LogDebug("API key retrieved from Preferences fallback");
                    return fallback;
                }
            }
            return value;
        }
        catch (Exception ex)
        {
            if (ShouldFallBackToPreferences())
            {
                _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}, checking Preferences fallback. Operation: GetApiKey", _platform);
                try
                {
                    var fallback = Preferences.Default.Get<string>(ConfigKeys.ApiKey, string.Empty);
                    return string.IsNullOrEmpty(fallback) ? null : fallback;
                }
                catch
                {
                    return null;
                }
            }

            _logger.LogError(ex, "SecureStorage unavailable on {Platform}. Cannot retrieve API key securely. Operation: GetApiKey", _platform);
            return null;
        }
    }

    public async Task SetApiKeyAsync(string apiKey)
    {
        // Edge Case 3: Validate not null
        ArgumentNullException.ThrowIfNull(apiKey, nameof(apiKey));

        // Edge Case 2: Validate not empty
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be null or empty", nameof(apiKey));
        }

        try
        {
            await SecureStorage.Default.SetAsync(ConfigKeys.ApiKey, apiKey);
        }
        catch (Exception ex)
        {
            if (ShouldFallBackToPreferences())
            {
                _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}, falling back to Preferences (less secure than DPAPI/Keychain). Operation: SetApiKey. Exception: {ExceptionType}",
                    _platform, ex.GetType().Name);

                try
                {
                    Preferences.Default.Set(ConfigKeys.ApiKey, apiKey);
                    _logger.LogInformation("API key stored in Preferences (fallback storage)");
                    return;
                }
                catch (Exception prefEx)
                {
                    _logger.LogError(prefEx, "Both SecureStorage and Preferences failed");
                    throw new SecureStorageUnavailableException(_platform, "SetApiKey",
                        $"Failed to store API key on {_platform}. SecureStorage: {ex.GetType().Name}, Preferences: {prefEx.GetType().Name}", ex);
                }
            }

            _logger.LogError(ex, "SecureStorage unavailable on {Platform}. Cannot store API key securely. Operation: SetApiKey", _platform);
            throw new SecureStorageUnavailableException(_platform, "SetApiKey",
                $"Failed to store API key securely on {_platform}. Preferences fallback is disabled on this platform.", ex);
        }
    }

    public async Task RemoveApiKeyAsync()
    {
        try
        {
            SecureStorage.Default.Remove(ConfigKeys.ApiKey);
            await Task.CompletedTask; // SecureStorage.Remove is synchronous, but interface is async
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}: Failed to remove API key. Operation: RemoveApiKey", _platform);
            // Don't throw on remove failures - best effort
        }

        // Also clear the Preferences fallback so a stale value isn't picked up later.
        try { Preferences.Default.Remove(ConfigKeys.ApiKey); } catch { }
    }

    #endregion

    #region HMAC Secret

    public async Task<string?> GetHmacSecretAsync()
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(ConfigKeys.HmacSecretKey);

            if (value == null && ShouldFallBackToPreferences())
            {
                var fallback = Preferences.Default.Get<string>(ConfigKeys.HmacSecretKey, string.Empty);
                if (!string.IsNullOrEmpty(fallback))
                {
                    _logger.LogDebug("HMAC secret retrieved from Preferences fallback");
                    return fallback;
                }
            }
            return value;
        }
        catch (Exception ex)
        {
            if (ShouldFallBackToPreferences())
            {
                _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}, checking Preferences fallback. Operation: GetHmacSecret", _platform);
                try
                {
                    var fallback = Preferences.Default.Get<string>(ConfigKeys.HmacSecretKey, string.Empty);
                    return string.IsNullOrEmpty(fallback) ? null : fallback;
                }
                catch
                {
                    return null;
                }
            }

            _logger.LogError(ex, "SecureStorage unavailable on {Platform}. Cannot retrieve HMAC secret securely. Operation: GetHmacSecret", _platform);
            return null;
        }
    }

    public async Task SetHmacSecretAsync(string hmacSecret)
    {
        // Edge Case 3: Validate not null
        ArgumentNullException.ThrowIfNull(hmacSecret, nameof(hmacSecret));

        // Edge Case 2: Validate not empty
        if (string.IsNullOrWhiteSpace(hmacSecret))
        {
            throw new ArgumentException("HMAC secret cannot be null or empty", nameof(hmacSecret));
        }

        hmacSecret = hmacSecret.Trim();

        try
        {
            await SecureStorage.Default.SetAsync(ConfigKeys.HmacSecretKey, hmacSecret);
        }
        catch (Exception ex)
        {
            if (ShouldFallBackToPreferences())
            {
                _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}, falling back to Preferences (less secure than DPAPI/Keychain). Operation: SetHmacSecret. Exception: {ExceptionType}",
                    _platform, ex.GetType().Name);

                try
                {
                    Preferences.Default.Set(ConfigKeys.HmacSecretKey, hmacSecret);
                    _logger.LogInformation("HMAC secret stored in Preferences (fallback storage)");
                    return;
                }
                catch (Exception prefEx)
                {
                    _logger.LogError(prefEx, "Both SecureStorage and Preferences failed");
                    throw new SecureStorageUnavailableException(_platform, "SetHmacSecret",
                        $"Failed to store HMAC secret on {_platform}. SecureStorage: {ex.GetType().Name}, Preferences: {prefEx.GetType().Name}", ex);
                }
            }

            _logger.LogError(ex, "SecureStorage unavailable on {Platform}. Cannot store HMAC secret securely. Operation: SetHmacSecret", _platform);
            throw new SecureStorageUnavailableException(_platform, "SetHmacSecret",
                $"Failed to store HMAC secret securely on {_platform}. Preferences fallback is disabled on this platform.", ex);
        }
    }

    public async Task RemoveHmacSecretAsync()
    {
        try
        {
            SecureStorage.Default.Remove(ConfigKeys.HmacSecretKey);
            await Task.CompletedTask; // SecureStorage.Remove is synchronous, but interface is async
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}: Failed to remove HMAC secret. Operation: RemoveHmacSecret", _platform);
            // Don't throw on remove failures - best effort
        }

        try { Preferences.Default.Remove(ConfigKeys.HmacSecretKey); } catch { }
    }

    #endregion

    #region Remove All

    public async Task RemoveAllAsync()
    {
        // Edge Case 8: RemoveAll when SecureStorage is empty should complete successfully
        try
        {
            SecureStorage.Default.Remove(ConfigKeys.ApiKey);
            SecureStorage.Default.Remove(ConfigKeys.HmacSecretKey);
            SecureStorage.Default.RemoveAll(); // Also clear any other keys (defensive)
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}: Failed to remove all credentials. Operation: RemoveAll", _platform);
            // Don't throw on remove failures - best effort
        }

        try { Preferences.Default.Remove(ConfigKeys.ApiKey); } catch { }
        try { Preferences.Default.Remove(ConfigKeys.HmacSecretKey); } catch { }
    }

    #endregion
}
