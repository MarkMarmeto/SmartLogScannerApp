# US0012: Implement Audio Feedback for Scan Results

> **Status:** Draft
> **Epic:** [EP0003: Scan Processing and Feedback](../epics/EP0003-scan-processing-and-feedback.md)
> **Owner:** Unassigned
> **Reviewer:** Unassigned
> **Created:** 2026-02-13

## User Story

**As a** Guard Gary
**I want** to hear a distinct sound for each scan result -- a pleasant beep for accepted, a double beep for duplicate, an error tone for rejected, and a soft chime for queued
**So that** I can confirm scan outcomes by ear without always looking at the screen, allowing me to maintain eye contact with students and keep the line moving

## Context

### Persona Reference
**Guard Gary** - School security guard, novice technical proficiency. Processes hundreds of students during peak gate hours. Audio confirmation lets him glance away while students pass. "I just need it to beep green or beep red."
[Full persona details](../personas.md#guard-gary)

### Background
During peak hours, Guard Gary cannot always look at the screen for every scan. Audio feedback provides a secondary confirmation channel -- he hears a sound and knows the result without looking. Each scan status maps to a distinct, memorable sound: a short pleasant beep for accepted (the most common), a double beep for duplicate (attention needed but not critical), a long error tone for rejected (stop the student), and a soft chime for offline-queued (informational). Audio playback must be non-blocking to avoid delaying the scan-to-result pipeline. Sound files are pre-loaded on app startup for near-zero latency playback. The feature can be toggled on/off via Preferences for environments where sound is not appropriate.

---

## Inherited Constraints

> See Epic for full constraint chain. Key constraints for this story:

| Source | Type | Constraint | AC Implication |
|--------|------|------------|----------------|
| PRD | Tech Stack | Plugin.Maui.Audio for cross-platform audio playback | Must use Plugin.Maui.Audio NuGet package; no platform-specific audio APIs |
| PRD | UX | Audio must not block scan processing | Fire-and-forget async playback; do not await completion before returning scan result |
| TRD | Architecture | Interface-based services for DI and testability | ISoundService interface wrapping Plugin.Maui.Audio; mockable in unit tests |
| Epic | Performance | Scan-to-result < 500ms total | Audio initialization must not add latency; pre-load files on app startup |
| PRD | Feature | Audio enable/disable via Preferences ("Scanner.SoundEnabled") | Check IPreferencesService.GetSoundEnabled() before each playback |

---

## Acceptance Criteria

### AC1: ACCEPTED scan plays success sound
- **Given** audio is enabled (Scanner.SoundEnabled = true)
- **When** a ScanResult with Status=Accepted is processed
- **Then** the sound file "success.wav" is played -- a short, pleasant beep lasting approximately 200-300ms

### AC2: DUPLICATE scan plays duplicate sound
- **Given** audio is enabled (Scanner.SoundEnabled = true)
- **When** a ScanResult with Status=Duplicate is processed
- **Then** the sound file "duplicate.wav" is played -- a double short beep pattern lasting approximately 400-500ms

### AC3: REJECTED scan plays error sound
- **Given** audio is enabled (Scanner.SoundEnabled = true)
- **When** a ScanResult with Status=Rejected is processed
- **Then** the sound file "error.wav" is played -- a longer error tone lasting approximately 500-700ms, clearly distinguishable from success and duplicate sounds

### AC4: QUEUED (offline) scan plays queued sound
- **Given** audio is enabled (Scanner.SoundEnabled = true)
- **When** a ScanResult with Status=Queued is processed
- **Then** the sound file "queued.wav" is played -- a soft chime lasting approximately 300-400ms, distinct from the other three sounds

### AC5: Audio disabled via Preferences suppresses all sounds
- **Given** audio is disabled (Scanner.SoundEnabled = false)
- **When** any ScanResult is processed
- **Then** no sound is played; the ISoundService.PlayResultSoundAsync() method returns immediately without attempting playback

### AC6: Sound files stored in Resources/Raw
- **Given** the application is built
- **When** the build output is inspected
- **Then** the four sound files (success.wav, duplicate.wav, error.wav, queued.wav) are present in the Resources/Raw/ directory and included as MauiAsset in the .csproj file

### AC7: Audio plays asynchronously without blocking scan processing
- **Given** a scan result has been received
- **When** ISoundService.PlayResultSoundAsync(ScanStatus) is called
- **Then** the method initiates playback and returns without waiting for the sound to finish playing; the calling code does not await the completion of the audio playback (fire-and-forget pattern with error handling)

### AC8: Audio files pre-loaded on app startup
- **Given** the application is starting up
- **When** ISoundService is initialized (e.g., via InitializeAsync() called during App.OnStart or MainViewModel initialization)
- **Then** all four sound files are loaded into memory via Plugin.Maui.Audio's IAudioManager.CreatePlayer(), so that subsequent playback calls have near-zero latency (no file I/O during scan processing)

---

## Scope

### In Scope
- ISoundService interface: InitializeAsync(), PlayResultSoundAsync(ScanStatus status), IsEnabled property
- SoundService concrete implementation wrapping Plugin.Maui.Audio (IAudioManager)
- Pre-loading of 4 .wav files on app startup into IAudioPlayer instances
- Status-to-sound mapping: Accepted -> success.wav, Duplicate -> duplicate.wav, Rejected -> error.wav, Queued -> queued.wav
- Preferences check (Scanner.SoundEnabled) before each playback
- Fire-and-forget async playback pattern with exception swallowing and logging
- DI registration of ISoundService as singleton in MauiProgram.cs
- Sound file placeholders in Resources/Raw/ (actual .wav files to be provided by UX/design)
- Unit tests for SoundService logic using mocked IAudioManager and IPreferencesService

### Out of Scope
- Custom audio file upload or configuration
- Volume control within the application (relies on system volume)
- Audio feedback for non-scan events (toggle change, connectivity change, etc.)
- Vibration or haptic feedback
- Sound file creation/design (placeholder .wav files used until UX provides final assets)
- Streaming audio or complex audio mixing

---

## Technical Notes

### Implementation Details
- **ISoundService** interface:
  ```csharp
  public interface ISoundService
  {
      Task InitializeAsync();
      Task PlayResultSoundAsync(ScanStatus status);
  }
  ```
- **SoundService** implementation:
  ```csharp
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

      public async Task InitializeAsync()
      {
          try
          {
              _successPlayer = _audioManager.CreatePlayer(
                  await FileSystem.OpenAppPackageFileAsync("success.wav"));
              _duplicatePlayer = _audioManager.CreatePlayer(
                  await FileSystem.OpenAppPackageFileAsync("duplicate.wav"));
              _errorPlayer = _audioManager.CreatePlayer(
                  await FileSystem.OpenAppPackageFileAsync("error.wav"));
              _queuedPlayer = _audioManager.CreatePlayer(
                  await FileSystem.OpenAppPackageFileAsync("queued.wav"));
              _initialized = true;
          }
          catch (Exception ex)
          {
              _logger.LogError(ex, "Failed to initialize audio players");
              _initialized = false;
          }
      }

      public Task PlayResultSoundAsync(ScanStatus status)
      {
          if (!_initialized || !_preferencesService.GetSoundEnabled())
              return Task.CompletedTask;

          var player = status switch
          {
              ScanStatus.Accepted => _successPlayer,
              ScanStatus.Duplicate => _duplicatePlayer,
              ScanStatus.Rejected => _errorPlayer,
              ScanStatus.Queued => _queuedPlayer,
              _ => null
          };

          if (player is null)
              return Task.CompletedTask;

          try
          {
              player.Play();
          }
          catch (Exception ex)
          {
              _logger.LogWarning(ex, "Audio playback failed for status {Status}", status);
          }

          return Task.CompletedTask;
      }
  }
  ```
- **Fire-and-forget** in MainViewModel:
  ```csharp
  // After receiving scan result:
  _ = _soundService.PlayResultSoundAsync(scanResult.Status);
  // Do NOT await -- continue with UI update immediately
  ```
- **DI registration:**
  ```csharp
  builder.Services.AddSingleton<ISoundService, SoundService>();
  builder.Services.AddSingleton(AudioManager.Current); // Plugin.Maui.Audio
  ```
- **Pre-loading** is called during app initialization:
  ```csharp
  // In App.OnStart or MainViewModel initialization
  var soundService = serviceProvider.GetRequiredService<ISoundService>();
  await soundService.InitializeAsync();
  ```

### API Contracts
Not applicable (no HTTP calls in this story).

### Data Requirements

**Sound File Mapping:**

| ScanStatus | Sound File | Description | Approximate Duration |
|------------|-----------|-------------|---------------------|
| Accepted | success.wav | Short pleasant beep | 200-300ms |
| Duplicate | duplicate.wav | Double short beep | 400-500ms |
| Rejected | error.wav | Long error tone | 500-700ms |
| Queued | queued.wav | Soft chime | 300-400ms |
| Error | (none) | No sound for Error status | N/A |
| RateLimited | (none) | No sound for RateLimited status | N/A |

**Preference Key:**

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| Scanner.SoundEnabled | bool | true | Whether audio feedback is active |

---

## Edge Cases & Error Handling

| Scenario | Expected Behaviour |
|----------|-------------------|
| Audio hardware unavailable (no speakers, audio device disconnected) | Plugin.Maui.Audio may throw on Play(); exception is caught and logged as a warning; scan processing continues unaffected; no user-facing error displayed |
| Sound file missing from Resources/Raw (e.g., success.wav deleted) | InitializeAsync() catches FileNotFoundException for the missing file; logs error with the file name; sets the corresponding player to null; other sounds still work; PlayResultSoundAsync for the missing sound is a no-op |
| Audio playback fails silently (driver error, OS-level issue) | Exception from player.Play() is caught and logged; no crash; no user-facing error; scan processing pipeline is unaffected |
| Rapid successive scans (3 scans within 1 second, overlapping audio) | Each scan triggers its own Play() call; Plugin.Maui.Audio handles concurrent playback at the platform level; if overlap is audible, it is acceptable (sounds are short enough that overlap is minimal) |
| Audio setting changed mid-scan (disabled while sound is playing) | Current playing sound continues to completion (cannot be stopped mid-play); next scan respects the new setting and does not play sound |
| Plugin.Maui.Audio initialization failure (IAudioManager unavailable) | InitializeAsync() catches the exception; _initialized remains false; all PlayResultSoundAsync calls are no-ops; error logged once at startup; app functions normally without audio |
| System volume at zero | Sounds play but are inaudible; this is expected OS-level behaviour; the application does not detect or warn about system volume level |

---

## Test Scenarios

- [ ] PlayResultSoundAsync(Accepted) calls Play() on the success IAudioPlayer when SoundEnabled is true
- [ ] PlayResultSoundAsync(Duplicate) calls Play() on the duplicate IAudioPlayer when SoundEnabled is true
- [ ] PlayResultSoundAsync(Rejected) calls Play() on the error IAudioPlayer when SoundEnabled is true
- [ ] PlayResultSoundAsync(Queued) calls Play() on the queued IAudioPlayer when SoundEnabled is true
- [ ] PlayResultSoundAsync does not call Play() on any player when SoundEnabled is false
- [ ] PlayResultSoundAsync returns without error when _initialized is false (pre-load failed)
- [ ] InitializeAsync loads all 4 audio files via FileSystem.OpenAppPackageFileAsync and IAudioManager.CreatePlayer
- [ ] InitializeAsync handles FileNotFoundException for a missing sound file gracefully (logs error, continues loading others)
- [ ] PlayResultSoundAsync catches and logs exception when player.Play() throws
- [ ] PlayResultSoundAsync with ScanStatus.Error or ScanStatus.RateLimited does not attempt playback (no mapped sound file)
- [ ] ISoundService is registered as singleton in DI container

---

## Dependencies

### Story Dependencies

| Story | Type | What's Needed | Status |
|-------|------|---------------|--------|
| US0001 | Requires | IPreferencesService for reading "Scanner.SoundEnabled" setting | Draft |

### External Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Plugin.Maui.Audio | NuGet Package | Available; provides IAudioManager and IAudioPlayer for cross-platform audio |
| .NET MAUI FileSystem API | Platform SDK | Available in .NET 8.0 MAUI; provides OpenAppPackageFileAsync for Resources/Raw files |
| Sound asset files (.wav) | Design Asset | Placeholder files needed; final assets from UX/design team |

---

## Estimation

**Story Points:** 3
**Complexity:** Low

---

## Open Questions

- [ ] Who provides the final .wav sound files? Should placeholder sounds be generated programmatically (sine wave tones) for development, or are stock sound files acceptable? - Owner: UX/Design
- [ ] Should there be a brief visual indicator (e.g., speaker icon flash) when audio is disabled so Guard Gary knows sounds are off? - Owner: UX
- [ ] If audio hardware is permanently unavailable (headless server deployment), should ISoundService expose an IsAvailable property so the settings UI can hide the toggle? - Owner: Architect
- [ ] Should the Error and RateLimited statuses also play sounds? Currently they are unmapped. - Owner: Product

---

## Revision History

| Date | Author | Change |
|------|--------|--------|
| 2026-02-13 | SDLC Studio | Initial story created |
