using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>
/// EP0011: Creates <see cref="CameraHeadlessWorker"/> instances for Mac Catalyst.
/// Passes through the shared <see cref="MacMultiCamSessionHost"/> so workers can
/// register on it when the OS supports multi-cam; otherwise the worker silently
/// falls back to its own per-instance <see cref="AVFoundation.AVCaptureSession"/>.
/// </summary>
public class CameraWorkerFactory : ICameraWorkerFactory
{
    private readonly ILogger<CameraHeadlessWorker>? _logger;
    private readonly MacMultiCamSessionHost _multiCamHost;

    public CameraWorkerFactory(
        MacMultiCamSessionHost multiCamHost,
        ILogger<CameraHeadlessWorker>? logger = null)
    {
        _multiCamHost = multiCamHost;
        _logger = logger;
    }

    public ICameraWorker Create() => new CameraHeadlessWorker(_logger, _multiCamHost);
}
