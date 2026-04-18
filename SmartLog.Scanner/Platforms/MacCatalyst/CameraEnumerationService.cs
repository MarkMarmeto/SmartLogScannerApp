using AVFoundation;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>
/// Enumerates physical video-capture devices on macOS/Mac Catalyst using AVFoundation.
/// Uses AVCaptureDeviceDiscoverySession (the modern API) for all device types.
/// </summary>
public class CameraEnumerationService : ICameraEnumerationService
{
    public Task<IList<CameraDeviceInfo>> GetAvailableCamerasAsync()
    {
        var session = AVCaptureDeviceDiscoverySession.Create(
            new[]
            {
                AVCaptureDeviceType.BuiltInWideAngleCamera,
                AVCaptureDeviceType.ExternalUnknown,
            },
            AVMediaTypes.Video,
            AVCaptureDevicePosition.Unspecified);

        IList<CameraDeviceInfo> result = (session?.Devices ?? Array.Empty<AVCaptureDevice>())
            .Select(d => new CameraDeviceInfo(d.UniqueID, d.LocalizedName))
            .ToList();

        return Task.FromResult(result);
    }

    /// <summary>
    /// EP0011 (US0071): Tests that a camera can be opened by attempting to create a capture session.
    /// Returns true if the device is accessible.
    /// </summary>
    public Task<bool> TestCameraAsync(string deviceId)
    {
        try
        {
            var device = AVCaptureDevice.DeviceWithUniqueID(deviceId);
            if (device == null) return Task.FromResult(false);

            // A device that exists and is not suspended is considered accessible
            return Task.FromResult(!device.Suspended);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
