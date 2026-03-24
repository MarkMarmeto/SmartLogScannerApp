using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Services;
using WinDevices = global::Windows.Devices.Enumeration;
using global::Windows.Media.Capture;

namespace SmartLog.Scanner.Platforms.Windows;

/// <summary>
/// Windows implementation of device detection using Windows.Media.Capture.
/// </summary>
public class DeviceDetectionService : IDeviceDetectionService
{
    private readonly ILogger<DeviceDetectionService> _logger;
    private ScanningMethod _detectedMethod = ScanningMethod.None;
    private List<CameraDevice> _detectedCameras = new();
    private bool _hasUsbKeyboard;

    public ScanningMethod DetectedMethod => _detectedMethod;
    public IReadOnlyList<CameraDevice> DetectedCameras => _detectedCameras.AsReadOnly();
    public bool HasUsbKeyboard => _hasUsbKeyboard;

    public DeviceDetectionService(ILogger<DeviceDetectionService> logger)
    {
        _logger = logger;
    }

    public async Task<ScanningMethod> DetectDevicesAsync()
    {
        _logger.LogInformation("Starting device detection (Windows)...");

        // Detect cameras
        await DetectCamerasAsync();

        // Detect USB keyboards (potential scanners)
        DetectUsbKeyboards();

        // Determine best scanning method
        _detectedMethod = DetermineScanningMethod();

        _logger.LogInformation("Device detection complete: {Method} ({CameraCount} cameras, USB keyboard: {HasUsb})",
            _detectedMethod, _detectedCameras.Count, _hasUsbKeyboard);

        return _detectedMethod;
    }

    private async Task DetectCamerasAsync()
    {
        _detectedCameras.Clear();

        try
        {
            // Find all video capture devices
            var devices = await WinDevices.DeviceInformation.FindAllAsync(WinDevices.DeviceClass.VideoCapture);

            if (devices != null && devices.Count > 0)
            {
                foreach (var device in devices)
                {
                    // Determine camera position (front/back/external)
                    var position = DetermineCameraPosition(device);

                    var camera = new CameraDevice
                    {
                        Id = device.Id,
                        Name = device.Name,
                        Position = position,
                        IsAvailable = device.IsEnabled
                    };

                    _detectedCameras.Add(camera);
                    _logger.LogInformation("Detected camera: {Name} ({Position}, Available: {Available})",
                        camera.Name, camera.Position, camera.IsAvailable);
                }
            }
            else
            {
                _logger.LogWarning("No cameras detected on Windows");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting cameras on Windows");
        }
    }

    private CameraPosition DetermineCameraPosition(WinDevices.DeviceInformation device)
    {
        var name = device.Name.ToLowerInvariant();

        // Try to infer position from device name
        if (name.Contains("front"))
        {
            return CameraPosition.Front;
        }
        else if (name.Contains("back") || name.Contains("rear"))
        {
            return CameraPosition.Back;
        }
        else if (name.Contains("usb") || name.Contains("external"))
        {
            return CameraPosition.External;
        }

        // Check device properties for EnclosureLocation
        if (device.EnclosureLocation != null)
        {
            var panel = device.EnclosureLocation.Panel;
            return panel switch
            {
                WinDevices.Panel.Front => CameraPosition.Front,
                WinDevices.Panel.Back => CameraPosition.Back,
                _ => CameraPosition.External
            };
        }

        // Default to external for desktop cameras
        return CameraPosition.External;
    }

    private void DetectUsbKeyboards()
    {
        try
        {
            // On Windows, we assume USB keyboards are always potentially available
            // A proper implementation would use Windows.Devices.HumanInterfaceDevice
            // For now, we default to true since most PCs have keyboards
            _hasUsbKeyboard = true;

            _logger.LogInformation("USB keyboard detection (Windows): {HasUsb}", _hasUsbKeyboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting USB keyboards on Windows");
            _hasUsbKeyboard = false;
        }
    }

    private ScanningMethod DetermineScanningMethod()
    {
        var hasCamera = _detectedCameras.Any(c => c.IsAvailable);

        if (hasCamera && _hasUsbKeyboard)
        {
            // Prefer camera when both are available
            return ScanningMethod.CameraWithUsbFallback;
        }
        else if (hasCamera)
        {
            return ScanningMethod.Camera;
        }
        else if (_hasUsbKeyboard)
        {
            return ScanningMethod.UsbScanner;
        }
        else
        {
            return ScanningMethod.None;
        }
    }

    public string GetDetectionSummary()
    {
        return _detectedMethod switch
        {
            ScanningMethod.Camera => $"Using camera: {_detectedCameras.FirstOrDefault()?.Name ?? "Default"}",
            ScanningMethod.UsbScanner => "Using USB barcode scanner (keyboard wedge)",
            ScanningMethod.CameraWithUsbFallback => $"Using camera ({_detectedCameras.FirstOrDefault()?.Name ?? "Default"}) with USB scanner fallback",
            ScanningMethod.None => "No suitable scanning device detected",
            _ => "Unknown scanning method"
        };
    }
}
