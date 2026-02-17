namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Service for encrypted credential storage via MAUI SecureStorage
/// (Keychain on macOS, DPAPI on Windows)
/// Stores API key and HMAC secret securely, never in plain text.
/// </summary>
public interface ISecureConfigService
{
    /// <summary>
    /// Retrieves the stored API key (X-API-Key header value)
    /// </summary>
    /// <returns>API key if set, null if not found or SecureStorage unavailable</returns>
    Task<string?> GetApiKeyAsync();

    /// <summary>
    /// Stores the API key securely
    /// </summary>
    /// <param name="apiKey">API key to store (cannot be null or empty)</param>
    /// <exception cref="ArgumentNullException">If apiKey is null</exception>
    /// <exception cref="ArgumentException">If apiKey is empty</exception>
    /// <exception cref="Exceptions.SecureStorageUnavailableException">If SecureStorage fails</exception>
    Task SetApiKeyAsync(string apiKey);

    /// <summary>
    /// Retrieves the stored HMAC-SHA256 shared secret
    /// </summary>
    /// <returns>HMAC secret if set, null if not found or SecureStorage unavailable</returns>
    Task<string?> GetHmacSecretAsync();

    /// <summary>
    /// Stores the HMAC-SHA256 shared secret securely
    /// </summary>
    /// <param name="hmacSecret">HMAC secret to store (cannot be null or empty)</param>
    /// <exception cref="ArgumentNullException">If hmacSecret is null</exception>
    /// <exception cref="ArgumentException">If hmacSecret is empty</exception>
    /// <exception cref="Exceptions.SecureStorageUnavailableException">If SecureStorage fails</exception>
    Task SetHmacSecretAsync(string hmacSecret);

    /// <summary>
    /// Removes the stored API key from SecureStorage
    /// </summary>
    Task RemoveApiKeyAsync();

    /// <summary>
    /// Removes the stored HMAC secret from SecureStorage
    /// </summary>
    Task RemoveHmacSecretAsync();

    /// <summary>
    /// Removes all stored credentials (API key and HMAC secret) from SecureStorage
    /// </summary>
    Task RemoveAllAsync();
}
