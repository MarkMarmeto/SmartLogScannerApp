namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// EP0011: Platform-specific factory that creates ICameraWorker instances.
/// Registered as a singleton; each Create() call returns a new independent worker.
/// </summary>
public interface ICameraWorkerFactory
{
    ICameraWorker Create();
}
