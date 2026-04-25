using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// EP0011 (US0066–US0070): Orchestrates 1–8 simultaneous camera QR scanner instances.
/// Each camera runs in isolation; a failure on one does not affect others.
/// </summary>
public interface IMultiCameraManager
{
    /// <summary>Current snapshot of all configured camera instances and their runtime state.</summary>
    IReadOnlyList<CameraInstance> Cameras { get; }

    /// <summary>
    /// Fired when a QR scan is fully accepted (optimistic) from a specific camera.
    /// Always raised on the originating thread — subscribers must marshal to the UI thread.
    /// </summary>
    event EventHandler<(int CameraIndex, ScanResult Result)>? ScanCompleted;

    /// <summary>
    /// Fired when the server confirms or corrects an optimistic scan result.
    /// Always raised on a background thread — subscribers must marshal to the UI thread.
    /// </summary>
    event EventHandler<(int CameraIndex, ScanResult Result)>? ScanUpdated;

    /// <summary>
    /// Fired when a camera's status changes (e.g., Scanning → Error → Offline).
    /// Always raised on a background thread — subscribers must marshal to the UI thread.
    /// </summary>
    event EventHandler<(int CameraIndex, CameraStatus Status)>? CameraStatusChanged;

    /// <summary>
    /// Initialises the manager with the supplied camera configurations.
    /// Cameras whose device ID is not physically available are set to Offline immediately.
    /// Must be called before StartAllAsync.
    /// </summary>
    /// <param name="cameras">Ordered list of camera configurations (max 8).</param>
    Task InitializeAsync(IReadOnlyList<CameraInstance> cameras);

    /// <summary>Starts all enabled cameras that are in Idle or Error state.</summary>
    Task StartAllAsync();

    /// <summary>
    /// Stops all running cameras and releases resources.
    /// Called on app close/navigate-away to ensure camera threads are cleaned up.
    /// </summary>
    Task StopAllAsync();

    /// <summary>
    /// Stops a specific camera and marks it as manually disabled (will NOT trigger auto-recovery).
    /// </summary>
    Task StopCameraAsync(int cameraIndex);

    /// <summary>
    /// Re-enables and restarts a specific camera (manual user-initiated restart or recovery restart).
    /// Resets ReconnectAttempts to 0.
    /// </summary>
    Task RestartCameraAsync(int cameraIndex);

    /// <summary>
    /// Routes a decoded QR payload from a specific camera to its scanner service for processing.
    /// </summary>
    Task ProcessQrCodeAsync(int cameraIndex, string payload);

    /// <summary>
    /// Recalculates adaptive throttle values for all cameras based on current active count
    /// and pushes the new skip count to each CameraInstance.
    /// </summary>
    void UpdateThrottleValues();

    /// <summary>
    /// Pushes the supplied device-level scan type to all running CameraQrScannerService instances.
    /// Takes effect on the next scan — no restart required.
    /// </summary>
    void UpdateScanTypes(string scanType);

    /// <summary>
    /// Propagates a renamed camera name to the running CameraQrScannerService for that slot.
    /// Call after the admin saves a new name in Setup so the next scan uses the updated name.
    /// </summary>
    void UpdateCameraName(int cameraIndex, string name);

    /// <summary>
    /// Passes the worker for the given camera index to the supplied action.
    /// Use this to attach a platform-specific preview without exposing worker internals to Core.
    /// No-op if the camera index is not found.
    /// </summary>
    void ConfigureCameraPreview(int cameraIndex, Action<ICameraWorker> configure);
}
