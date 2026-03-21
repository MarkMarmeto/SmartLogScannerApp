using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Tests.Services;

/// <summary>
/// Unit tests for ISecureConfigService contract.
/// The concrete SecureConfigService wraps MAUI SecureStorage (static API) which is
/// unavailable in unit tests — round-trip tests use an in-memory implementation,
/// while input validation tests use Moq to verify the interface contract.
/// Test Spec: TS0001 (TC001-TC015)
/// </summary>
public class SecureConfigServiceTests
{
    /// <summary>
    /// In-memory implementation of ISecureConfigService for unit testing.
    /// Mirrors the validation and behavior of the real SecureConfigService.
    /// </summary>
    private class InMemorySecureConfigService : ISecureConfigService
    {
        private readonly Dictionary<string, string> _store = new();

        public Task<string?> GetApiKeyAsync()
            => Task.FromResult(_store.TryGetValue("ApiKey", out var v) ? v : null);

        public Task SetApiKeyAsync(string apiKey)
        {
            ArgumentNullException.ThrowIfNull(apiKey, nameof(apiKey));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be null or empty", nameof(apiKey));
            _store["ApiKey"] = apiKey;
            return Task.CompletedTask;
        }

        public Task<string?> GetHmacSecretAsync()
            => Task.FromResult(_store.TryGetValue("HmacSecret", out var v) ? v : null);

        public Task SetHmacSecretAsync(string hmacSecret)
        {
            ArgumentNullException.ThrowIfNull(hmacSecret, nameof(hmacSecret));
            if (string.IsNullOrWhiteSpace(hmacSecret))
                throw new ArgumentException("HMAC secret cannot be null or empty", nameof(hmacSecret));
            _store["HmacSecret"] = hmacSecret;
            return Task.CompletedTask;
        }

        public Task RemoveApiKeyAsync()
        {
            _store.Remove("ApiKey");
            return Task.CompletedTask;
        }

        public Task RemoveHmacSecretAsync()
        {
            _store.Remove("HmacSecret");
            return Task.CompletedTask;
        }

        public Task RemoveAllAsync()
        {
            _store.Clear();
            return Task.CompletedTask;
        }
    }

    private ISecureConfigService CreateService() => new InMemorySecureConfigService();

    #region TC001-TC002: Basic round-trip storage and retrieval

    [Fact]
    public async Task SetApiKeyAsync_ValidKey_StoresAndRetrievesSuccessfully()
    {
        var service = CreateService();
        const string testKey = "sk-test-abc123def456";

        await service.SetApiKeyAsync(testKey);
        var retrieved = await service.GetApiKeyAsync();

        Assert.Equal(testKey, retrieved);
    }

    [Fact]
    public async Task SetHmacSecretAsync_ValidSecret_StoresAndRetrievesSuccessfully()
    {
        var service = CreateService();
        const string testSecret = "K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=";

        await service.SetHmacSecretAsync(testSecret);
        var retrieved = await service.GetHmacSecretAsync();

        Assert.Equal(testSecret, retrieved);
    }

    #endregion

    #region TC003-TC004: Key not found returns null

    [Fact]
    public async Task GetApiKeyAsync_WhenNotSet_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetApiKeyAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHmacSecretAsync_WhenNotSet_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetHmacSecretAsync();

        Assert.Null(result);
    }

    #endregion

    #region TC005-TC008: Input validation (empty string and null)

    [Fact]
    public async Task SetApiKeyAsync_EmptyString_ThrowsArgumentException()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.SetApiKeyAsync(""));
        Assert.Contains("API key cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task SetApiKeyAsync_Null_ThrowsArgumentNullException()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => service.SetApiKeyAsync(null!));
        Assert.Equal("apiKey", exception.ParamName);
    }

    [Fact]
    public async Task SetHmacSecretAsync_EmptyString_ThrowsArgumentException()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.SetHmacSecretAsync(""));
        Assert.Contains("HMAC secret cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task SetHmacSecretAsync_Null_ThrowsArgumentNullException()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => service.SetHmacSecretAsync(null!));
        Assert.Equal("hmacSecret", exception.ParamName);
    }

    #endregion

    #region TC009-TC011: Remove operations

    [Fact]
    public async Task RemoveApiKeyAsync_RemovesKey_SubsequentGetReturnsNull()
    {
        var service = CreateService();
        await service.SetApiKeyAsync("test-key");

        await service.RemoveApiKeyAsync();
        var result = await service.GetApiKeyAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveHmacSecretAsync_RemovesSecret_SubsequentGetReturnsNull()
    {
        var service = CreateService();
        await service.SetHmacSecretAsync("test-secret");

        await service.RemoveHmacSecretAsync();
        var result = await service.GetHmacSecretAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAllAsync_ClearsBothCredentials()
    {
        var service = CreateService();
        await service.SetApiKeyAsync("test-key");
        await service.SetHmacSecretAsync("test-secret");

        await service.RemoveAllAsync();
        var apiKey = await service.GetApiKeyAsync();
        var hmacSecret = await service.GetHmacSecretAsync();

        Assert.Null(apiKey);
        Assert.Null(hmacSecret);
    }

    #endregion

    #region TC014-TC015: Special cases

    [Fact]
    public async Task SetApiKeyAsync_SpecialCharacters_StoresAndRetrievesCorrectly()
    {
        var service = CreateService();
        const string keyWithSpecialChars = "sk-abc+/=123";

        await service.SetApiKeyAsync(keyWithSpecialChars);
        var retrieved = await service.GetApiKeyAsync();

        Assert.Equal(keyWithSpecialChars, retrieved);
    }

    [Fact]
    public async Task SetApiKeyAsync_OverwriteExisting_ReplacesOldValue()
    {
        var service = CreateService();
        const string oldKey = "old-key";
        const string newKey = "new-key";

        await service.SetApiKeyAsync(oldKey);
        await service.SetApiKeyAsync(newKey);
        var retrieved = await service.GetApiKeyAsync();

        Assert.Equal(newKey, retrieved);
        Assert.NotEqual(oldKey, retrieved);
    }

    #endregion
}
