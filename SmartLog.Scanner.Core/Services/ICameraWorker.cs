namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// EP0011: Headless camera worker — manages one physical camera's capture session
/// and fires QrCodeDetected when a QR code is found.
///
/// Implementations must not create any native UI view or preview layer.
/// Platform services create instances via ICameraWorkerFactory.
/// </summary>
public interface ICameraWorker : IAsyncDisposable
{
    /// <summary>Fired on a background thread when a QR code is decoded.</summary>
    event EventHandler<string>? QrCodeDetected;

    /// <summary>Fired when the camera encounters an unrecoverable error.</summary>
    event EventHandler<string>? ErrorOccurred;

    /// <summary>True when the camera session is running.</summary>
    bool IsRunning { get; }

    /// <summary>Starts the capture session for the given device ID. Pass null for the default camera.</summary>
    Task StartAsync(string? deviceId = null);

    /// <summary>Stops the capture session and releases hardware resources.</summary>
    Task StopAsync();
}
