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
            return await SecureStorage.Default.GetAsync(ConfigKeys.ApiKey);
        }
        catch (Exception ex)
        {
            // Fallback to Preferences for development builds
            _logger.LogWarning(ex, "SecureStorage unavailable, checking Preferences fallback. Operation: GetApiKey");
            try
            {
                return Preferences.Default.Get<string?>(ConfigKeys.ApiKey, null);
            }
            catch
            {
                return null;
            }
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
            // Fallback to Preferences for development builds without proper entitlements
            _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}, falling back to Preferences (INSECURE - development only). Operation: SetApiKey. Exception: {ExceptionType}",
                _platform, ex.GetType().Name);

            try
            {
                Preferences.Default.Set(ConfigKeys.ApiKey, apiKey);
                _logger.LogInformation("API key stored in Preferences (fallback storage)");
            }
            catch (Exception prefEx)
            {
                _logger.LogError(prefEx, "Both SecureStorage and Preferences failed");
                throw new SecureStorageUnavailableException(_platform, "SetApiKey",
                    $"Failed to store API key on {_platform}. SecureStorage: {ex.GetType().Name}, Preferences: {prefEx.GetType().Name}", ex);
            }
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
            return await SecureStorage.Default.GetAsync(ConfigKeys.HmacSecretKey);
        }
        catch (Exception ex)
        {
            // Fallback to Preferences for development builds
            _logger.LogWarning(ex, "SecureStorage unavailable, checking Preferences fallback. Operation: GetHmacSecret");
            try
            {
                return Preferences.Default.Get<string?>(ConfigKeys.HmacSecretKey, null);
            }
            catch
            {
                return null;
            }
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
            // Fallback to Preferences for development builds without proper entitlements
            _logger.LogWarning(ex, "SecureStorage unavailable on {Platform}, falling back to Preferences (INSECURE - development only). Operation: SetHmacSecret",
                _platform);

            try
            {
                Preferences.Default.Set(ConfigKeys.HmacSecretKey, hmacSecret);
                _logger.LogInformation("HMAC secret stored in Preferences (fallback storage)");
            }
            catch (Exception prefEx)
            {
                _logger.LogError(prefEx, "Both SecureStorage and Preferences failed");
                throw new SecureStorageUnavailableException(_platform, "SetHmacSecret",
                    $"Failed to store HMAC secret on {_platform}. SecureStorage: {ex.GetType().Name}, Preferences: {prefEx.GetType().Name}", ex);
            }
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
