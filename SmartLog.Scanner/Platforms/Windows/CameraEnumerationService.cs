using Windows.Devices.Enumeration;
using SmartLog.Scanner.Core.Models;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Platforms.Windows;

/// <summary>
/// Enumerates physical video-capture devices on Windows using the WinRT DeviceInformation API.
/// </summary>
public class CameraEnumerationService : ICameraEnumerationService
{
    public async Task<IList<CameraDeviceInfo>> GetAvailableCamerasAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return devices
            .Select(d => new CameraDeviceInfo(d.Id, d.Name))
            .ToList();
    }
}
