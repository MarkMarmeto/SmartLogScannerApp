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

    #region API Key

    public async Task<string?> GetApiKeyAsync()
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(ConfigKeys.ApiKey);
#if DEBUG
            // If SecureStorage returned null, also check Preferences fallback
            // (key may have been saved to Preferences when SecureStorage threw on Set)
            if (value == null)
            {
                value = Preferences.Default.Get<string>(ConfigKeys.ApiKey, string.Empty);
                if (!string.IsNullOrEmpty(value))
                {
                    _logger.LogDebug("API key retrieved from Preferences fallback (DEBUG mode)");
                }
                else
                {
                    value = null;
                }
            }
#endif
            return value;
        }
        catch (Exception ex)
        {
#if DEBUG
            // SECURITY: Fallback to Preferences for development builds ONLY
            // In production, we fail fast to prevent insecure storage
            _logger.LogWarning(ex, "SecureStorage unavailable, checking Preferences fallback (DEBUG mode only). Operation: GetApiKey");
            try
            {
                var fallback = Preferences.Default.Get<string>(ConfigKeys.ApiKey, string.Empty);
                return string.IsNullOrEmpty(fallback) ? null : fallback;
            }
            catch
            {
                return null;
            }
#else
            // PRODUCTION: Fail fast - do not use insecure Preferences storage
            _logger.LogError(ex, "SecureStorage unavailable on {Platform}. Cannot retrieve API key securely. Operation: GetApiKey", _platform);
            return null;
#endif
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
#if DEBUG
            // SECURITY: Fallback to Preferences for development builds ONLY without proper entitlements
            // In production, we fail fast to prevent insecure storage
            _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}, falling back to Preferences (INSECURE - DEBUG mode only). Operation: SetApiKey. Exception: {ExceptionType}",
                _platform, ex.GetType().Name);

            try
            {
                Preferences.Default.Set(ConfigKeys.ApiKey, apiKey);
                _logger.LogInformation("API key stored in Preferences (fallback storage - DEBUG mode)");
            }
            catch (Exception prefEx)
            {
                _logger.LogError(prefEx, "Both SecureStorage and Preferences failed");
                throw new SecureStorageUnavailableException(_platform, "SetApiKey",
                    $"Failed to store API key on {_platform}. SecureStorage: {ex.GetType().Name}, Preferences: {prefEx.GetType().Name}", ex);
            }
#else
            // PRODUCTION: Fail fast - do not use insecure Preferences storage
            _logger.LogError(ex, "SecureStorage unavailable on {Platform}. Cannot store API key securely. Operation: SetApiKey", _platform);
            throw new SecureStorageUnavailableException(_platform, "SetApiKey",
                $"Failed to store API key securely on {_platform}. Preferences fallback is disabled in production builds.", ex);
#endif
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
    }

    #endregion

    #region HMAC Secret

    public async Task<string?> GetHmacSecretAsync()
    {
        try
        {
            var value = await SecureStorage.Default.GetAsync(ConfigKeys.HmacSecretKey);
#if DEBUG
            // If SecureStorage returned null, also check Preferences fallback
            // (key may have been saved to Preferences when SecureStorage threw on Set)
            if (value == null)
            {
                value = Preferences.Default.Get<string>(ConfigKeys.HmacSecretKey, string.Empty);
                if (!string.IsNullOrEmpty(value))
                {
                    _logger.LogDebug("HMAC secret retrieved from Preferences fallback (DEBUG mode)");
                }
                else
                {
                    value = null;
                }
            }
#endif
            return value;
        }
        catch (Exception ex)
        {
#if DEBUG
            // SECURITY: Fallback to Preferences for development builds ONLY
            // In production, we fail fast to prevent insecure storage
            _logger.LogWarning(ex, "SecureStorage unavailable, checking Preferences fallback (DEBUG mode only). Operation: GetHmacSecret");
            try
            {
                var fallback = Preferences.Default.Get<string>(ConfigKeys.HmacSecretKey, string.Empty);
                return string.IsNullOrEmpty(fallback) ? null : fallback;
            }
            catch
            {
                return null;
            }
#else
            // PRODUCTION: Fail fast - do not use insecure Preferences storage
            _logger.LogError(ex, "SecureStorage unavailable on {Platform}. Cannot retrieve HMAC secret securely. Operation: GetHmacSecret", _platform);
            return null;
#endif
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

        try
        {
            await SecureStorage.Default.SetAsync(ConfigKeys.HmacSecretKey, hmacSecret);
        }
        catch (Exception ex)
        {
#if DEBUG
            // SECURITY: Fallback to Preferences for development builds ONLY without proper entitlements
            // In production, we fail fast to prevent insecure storage
            _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}, falling back to Preferences (INSECURE - DEBUG mode only). Operation: SetHmacSecret",
                _platform);

            try
            {
                Preferences.Default.Set(ConfigKeys.HmacSecretKey, hmacSecret);
                _logger.LogInformation("HMAC secret stored in Preferences (fallback storage - DEBUG mode)");
            }
            catch (Exception prefEx)
            {
                _logger.LogError(prefEx, "Both SecureStorage and Preferences failed");
                throw new SecureStorageUnavailableException(_platform, "SetHmacSecret",
                    $"Failed to store HMAC secret on {_platform}. SecureStorage: {ex.GetType().Name}, Preferences: {prefEx.GetType().Name}", ex);
            }
#else
            // PRODUCTION: Fail fast - do not use insecure Preferences storage
            _logger.LogError(ex, "SecureStorage unavailable on {Platform}. Cannot store HMAC secret securely. Operation: SetHmacSecret", _platform);
            throw new SecureStorageUnavailableException(_platform, "SetHmacSecret",
                $"Failed to store HMAC secret securely on {_platform}. Preferences fallback is disabled in production builds.", ex);
#endif
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
    }

    #endregion
}
