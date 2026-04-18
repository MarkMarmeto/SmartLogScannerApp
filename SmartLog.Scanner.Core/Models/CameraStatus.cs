namespace SmartLog.Scanner.Core.Models;

/// <summary>
/// EP0011 (US0070): Lifecycle state of a single camera instance.
/// </summary>
public enum CameraStatus
{
    /// <summary>Camera is configured but not yet started.</summary>
    Idle,

    /// <summary>Camera is actively scanning for QR codes.</summary>
    Scanning,

    /// <summary>Camera encountered an error. Auto-recovery may be in progress.</summary>
    Error,

    /// <summary>Camera is offline — all auto-recovery attempts exhausted, or device not found at startup.</summary>
    Offline,

    /// <summary>Camera pipeline is running but no frames have been received recently (possible hardware issue).</summary>
    NoSignal
}
