using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// SECURITY FIX (CRITICAL-01): Migration service to move secrets from config.json to SecureStorage.
/// This service runs once on app startup to migrate existing installations.
/// </summary>
public class SecurityMigrationService
{
    private readonly ISecureConfigService _secureConfig;
    private readonly ILogger<SecurityMigrationService> _logger;
    private readonly string _configFilePath;

    public SecurityMigrationService(
        ISecureConfigService secureConfig,
        ILogger<SecurityMigrationService> logger)
    {
        _secureConfig = secureConfig;
        _logger = logger;
        _configFilePath = Path.Combine(FileSystem.AppDataDirectory, "config.json");
    }

    /// <summary>
    /// Migrates ApiKey and HmacSecret from config.json to SecureStorage.
    /// Safe to call multiple times - idempotent operation.
    /// </summary>
    public async Task MigrateSecretsAsync()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                _logger.LogInformation("No config file to migrate");
                return;
            }

            var json = await File.ReadAllTextAsync(_configFilePath);
            var config = JsonSerializer.Deserialize<LegacyAppConfig>(json);

            if (config == null)
            {
                _logger.LogWarning("Could not parse config file for migration");
                return;
            }

            var migrated = false;

            // Migrate API Key if present in file AND not already configured
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                var existing = await _secureConfig.GetApiKeyAsync();
                if (string.IsNullOrWhiteSpace(existing))
                {
                    _logger.LogInformation("Migrating API key from config.json to SecureStorage");
                    await _secureConfig.SetApiKeyAsync(config.ApiKey);
                    migrated = true;
                }
                else
                {
                    _logger.LogInformation("API key already configured in SecureStorage, skipping migration");
                    migrated = true; // Still clean up config.json
                }
            }

            // Migrate HMAC Secret if present in file AND not already configured
            if (!string.IsNullOrWhiteSpace(config.HmacSecret))
            {
                var existing = await _secureConfig.GetHmacSecretAsync();
                if (string.IsNullOrWhiteSpace(existing))
                {
                    _logger.LogInformation("Migrating HMAC secret from config.json to SecureStorage");
                    await _secureConfig.SetHmacSecretAsync(config.HmacSecret);
                    migrated = true;
                }
                else
                {
                    _logger.LogInformation("HMAC secret already configured in SecureStorage, skipping migration");
                    migrated = true; // Still clean up config.json
                }
            }

            if (migrated)
            {
                // Clean the config file by removing secrets and re-saving
                await CleanConfigFileAsync(config);
                _logger.LogInformation("✅ Security migration complete - secrets moved to SecureStorage");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during security migration");
            // Don't throw - app should continue even if migration fails
        }
    }

    /// <summary>
    /// Removes secrets from config.json and saves the cleaned version.
    /// </summary>
    private async Task CleanConfigFileAsync(LegacyAppConfig oldConfig)
    {
        try
        {
            // Create new config without secrets
            var cleanConfig = new
            {
                oldConfig.ServerUrl,
                oldConfig.DeviceId,
                oldConfig.DeviceName,
                oldConfig.ScanMode,
                oldConfig.DefaultScanType,
                oldConfig.SetupCompleted,
                oldConfig.SoundEnabled,
                oldConfig.AcceptSelfSignedCerts
                // ApiKey and HmacSecret intentionally omitted
            };

            var json = JsonSerializer.Serialize(cleanConfig, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_configFilePath, json);
            _logger.LogInformation("Config file cleaned - secrets removed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning config file");
        }
    }

    /// <summary>
    /// Legacy config model that includes the old ApiKey/HmacSecret fields.
    /// Used only for migration purposes.
    /// </summary>
    private class LegacyAppConfig
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string HmacSecret { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string ScanMode { get; set; } = "Camera";
        public string DefaultScanType { get; set; } = "ENTRY";
        public bool SetupCompleted { get; set; } = false;
        public bool SoundEnabled { get; set; } = true;
        public bool AcceptSelfSignedCerts { get; set; } = false;
    }
}
