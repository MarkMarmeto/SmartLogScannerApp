namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Service for detecting available scanning input devices.
/// Automatically determines the best scanning method based on hardware.
/// </summary>
public interface IDeviceDetectionService
{
    /// <summary>
    /// Detected scanning method based on available hardware.
    /// </summary>
    ScanningMethod DetectedMethod { get; }

    /// <summary>
    /// List of detected camera devices.
    /// </summary>
    IReadOnlyList<CameraDevice> DetectedCameras { get; }

    /// <summary>
    /// Whether any USB keyboard (potential scanner) is connected.
    /// </summary>
    bool HasUsbKeyboard { get; }

    /// <summary>
    /// Detects available scanning input devices.
    /// </summary>
    Task<ScanningMethod> DetectDevicesAsync();

    /// <summary>
    /// Gets a human-readable description of detected devices.
    /// </summary>
    string GetDetectionSummary();
}

/// <summary>
/// Represents a detected camera device.
/// </summary>
public record CameraDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required CameraPosition Position { get; init; }
    public required bool IsAvailable { get; init; }
}

/// <summary>
/// Camera position (front-facing, back-facing, or external).
/// </summary>
public enum CameraPosition
{
    Unknown,
    Front,
    Back,
    External
}

/// <summary>
/// Automatically determined scanning method.
/// </summary>
public enum ScanningMethod
{
    /// <summary>
    /// No suitable input device detected.
    /// </summary>
    None,

    /// <summary>
    /// Camera available - use camera-based QR scanning.
    /// </summary>
    Camera,

    /// <summary>
    /// USB keyboard detected - use keyboard wedge scanner.
    /// </summary>
    UsbScanner,

    /// <summary>
    /// Both camera and USB available - prefer camera.
    /// </summary>
    CameraWithUsbFallback
}
