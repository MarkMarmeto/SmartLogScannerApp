using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Platforms.Windows;

/// <summary>EP0011: Creates CameraHeadlessWorker instances for Windows.</summary>
public class CameraWorkerFactory : ICameraWorkerFactory
{
    public ICameraWorker Create() => new CameraHeadlessWorker();
}
