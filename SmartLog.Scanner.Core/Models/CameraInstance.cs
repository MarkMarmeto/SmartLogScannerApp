namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// EP0011 (US0066): Represents configuration and runtime state of a single camera in the multi-camera setup.
/// </summary>
public class CameraInstance
{
    /// <summary>Zero-based position in the camera grid.</summary>
    public int Index { get; set; }

    /// <summary>Platform-specific device ID (e.g., from IDeviceDetectionService).</summary>
    public string CameraDeviceId { get; set; } = string.Empty;

    /// <summary>User-facing name, e.g. "Gate A".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>"ENTRY" or "EXIT" — per-camera scan direction.</summary>
    public string ScanType { get; set; } = "ENTRY";

    /// <summary>Whether this camera slot is enabled by the admin.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Current lifecycle state.</summary>
    public CameraStatus Status { get; set; } = CameraStatus.Idle;

    /// <summary>Last error message, if Status == Error or NoSignal.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Adaptive frame-skip count calculated by AdaptiveDecodeThrottle.
    /// Platform handler only forwards a barcode event every N-th frame.
    /// </summary>
    public int DecodeThrottleFrames { get; set; } = 5;

    /// <summary>Measured frame rate (updated by a 1-second timer in the UI layer).</summary>
    public double FrameRate { get; set; }

    /// <summary>Last time a barcode event was processed (for watchdog "no signal" detection).</summary>
    public DateTime? LastDecodeAt { get; set; }

    /// <summary>
    /// Last time any frame was received from the camera pipeline.
    /// Updated by the platform handler on every frame (not just decoded ones).
    /// Used by the watchdog to detect camera hangs and no-signal conditions.
    /// </summary>
    public DateTime? LastFrameAt { get; set; }

    /// <summary>Number of auto-recovery attempts made since last error. Reset on successful restart.</summary>
    public int ReconnectAttempts { get; set; }
}
