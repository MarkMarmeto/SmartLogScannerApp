using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Services;

/// <summary>
/// US0012: Audio feedback service using Plugin.Maui.Audio.
/// Pre-loads sound files on startup for near-zero latency playback.
/// </summary>
public class SoundService : ISoundService
{
    private readonly IAudioManager _audioManager;
    private readonly IPreferencesService _preferencesService;
    private readonly ILogger<SoundService> _logger;

    private IAudioPlayer? _successPlayer;
    private IAudioPlayer? _duplicatePlayer;
    private IAudioPlayer? _errorPlayer;
    private IAudioPlayer? _queuedPlayer;
    private bool _initialized;

    public SoundService(
        IAudioManager audioManager,
        IPreferencesService preferencesService,
        ILogger<SoundService> logger)
    {
        _audioManager = audioManager;
        _preferencesService = preferencesService;
        _logger = logger;
    }

    /// <summary>
    /// AC8: Pre-load all sound files into memory on app startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing audio players...");

            // AC6: Load sound files from Resources/Raw
            _successPlayer = _audioManager.CreatePlayer(
                await FileSystem.OpenAppPackageFileAsync("success.wav"));
            _duplicatePlayer = _audioManager.CreatePlayer(
                await FileSystem.OpenAppPackageFileAsync("duplicate.wav"));
            _errorPlayer = _audioManager.CreatePlayer(
                await FileSystem.OpenAppPackageFileAsync("error.wav"));
            _queuedPlayer = _audioManager.CreatePlayer(
                await FileSystem.OpenAppPackageFileAsync("queued.wav"));

            _initialized = true;
            _logger.LogInformation("Audio players initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize audio players - audio feedback will be disabled");
            _initialized = false;
        }
    }

    /// <summary>
    /// AC1-AC7: Play appropriate sound based on scan status.
    /// Fire-and-forget pattern with no blocking.
    /// </summary>
    public async Task PlayResultSoundAsync(ScanStatus status)
    {
        // AC5: Check if audio is enabled
        if (!_preferencesService.GetSoundEnabled())
        {
            _logger.LogDebug("Audio disabled via preferences - skipping playback");
            return;
        }

        if (!_initialized)
        {
            _logger.LogWarning("Audio players not initialized - cannot play sound");
            return;
        }

        // AC7: Fire-and-forget pattern - don't await
        _ = Task.Run(async () =>
        {
            try
            {
                IAudioPlayer? player = status switch
                {
                    ScanStatus.Accepted => _successPlayer,    // AC1
                    ScanStatus.Duplicate => _duplicatePlayer,  // AC2
                    ScanStatus.Rejected => _errorPlayer,       // AC3
                    ScanStatus.Error => _errorPlayer,          // Same as rejected
                    ScanStatus.Queued => _queuedPlayer,        // AC4
                    ScanStatus.RateLimited => _queuedPlayer,   // Same as queued
                    _ => null
                };

                if (player != null)
                {
                    // Reset to start and play
                    player.Seek(0);
                    player.Play();

                    _logger.LogDebug("Playing {Status} sound", status);
                }
            }
            catch (Exception ex)
            {
                // AC7: Swallow exceptions to prevent crashes from audio failures
                _logger.LogError(ex, "Error playing {Status} sound", status);
            }

            await Task.CompletedTask;
        });

        await Task.CompletedTask;
    }
}
