using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>EP0011: Creates CameraHeadlessWorker instances for Mac Catalyst.</summary>
public class CameraWorkerFactory : ICameraWorkerFactory
{
    private readonly ILogger<CameraHeadlessWorker>? _logger;

    public CameraWorkerFactory(ILogger<CameraHeadlessWorker>? logger = null)
    {
        _logger = logger;
    }

    public ICameraWorker Create() => new CameraHeadlessWorker(_logger);
}
