using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// File-based configuration service for reliable persistence.
/// Stores configuration in a JSON file in the app data directory.
/// </summary>
public class FileConfigService
{
    private readonly ILogger<FileConfigService> _logger;
    private readonly string _configFilePath;
    private AppConfig? _cachedConfig;

    public FileConfigService(ILogger<FileConfigService> logger)
    {
        _logger = logger;
        var appDataDir = FileSystem.AppDataDirectory;
        _configFilePath = Path.Combine(appDataDir, "config.json");
        _logger.LogInformation("Config file path: {Path}", _configFilePath);
    }

    public async Task<AppConfig> LoadConfigAsync()
    {
        if (_cachedConfig != null)
            return _cachedConfig;

        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath);
                _cachedConfig = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                _logger.LogInformation("Configuration loaded from file");
            }
            else
            {
                _cachedConfig = new AppConfig();
                _logger.LogInformation("No config file found, using defaults");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration, using defaults");
            _cachedConfig = new AppConfig();
        }

        return _cachedConfig;
    }

    public async Task SaveConfigAsync(AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_configFilePath, json);
            _cachedConfig = config;
            _logger.LogInformation("Configuration saved to file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration");
            throw;
        }
    }

    public async Task ClearConfigAsync()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                File.Delete(_configFilePath);
                _cachedConfig = null;
                _logger.LogInformation("Configuration file deleted");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting configuration");
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// Application configuration model.
/// </summary>
public class AppConfig
{
    public string ServerUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string HmacSecret { get; set; } = string.Empty;
    public string ScanMode { get; set; } = "Camera";
    public string DefaultScanType { get; set; } = "ENTRY";
    public bool SetupCompleted { get; set; } = false;
    public bool SoundEnabled { get; set; } = true;
}
