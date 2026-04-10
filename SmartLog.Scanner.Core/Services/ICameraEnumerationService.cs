using SmartLog.Scanner.Core.Models;

namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// Enumerates the physical camera devices available on the host machine.
/// Implemented per-platform; injected into SetupViewModel via DI.
/// </summary>
public interface ICameraEnumerationService
{
    /// <summary>
    /// Returns all video-capture devices attached to this machine.
    /// Returns an empty list when no cameras are present.
    /// </summary>
    Task<IList<CameraDeviceInfo>> GetAvailableCamerasAsync();
}
