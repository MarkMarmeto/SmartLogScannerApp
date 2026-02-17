using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0012: Service for playing audio feedback for scan results.
/// Provides distinct sounds for ACCEPTED, DUPLICATE, REJECTED, and QUEUED statuses.
/// </summary>
public interface ISoundService
{
    /// <summary>
    /// Initializes and pre-loads all sound files into memory.
    /// Should be called during app startup.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Plays the appropriate sound for the given scan status.
    /// Fire-and-forget pattern - does not wait for playback to complete.
    /// </summary>
    /// <param name="status">Scan result status</param>
    Task PlayResultSoundAsync(ScanStatus status);
}
