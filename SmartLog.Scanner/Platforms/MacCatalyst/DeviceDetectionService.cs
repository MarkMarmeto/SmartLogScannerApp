using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Services;
using AVFoundation;
using Foundation;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>
/// macOS/MacCatalyst implementation of device detection using AVFoundation.
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
        _logger.LogInformation("Starting device detection...");

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
            // Request camera permission
            var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
            if (status == AVAuthorizationStatus.NotDetermined)
            {
                // Request permission
                var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
                if (!granted)
                {
                    _logger.LogWarning("Camera permission denied by user");
                    return;
                }
            }
            else if (status != AVAuthorizationStatus.Authorized)
            {
                _logger.LogWarning("Camera permission not authorized: {Status}", status);
                return;
            }

            // Discover all video devices
            var discoverySession = AVCaptureDeviceDiscoverySession.Create(
                new[] { AVCaptureDeviceType.BuiltInWideAngleCamera },
                AVMediaTypes.Video,
                AVCaptureDevicePosition.Unspecified);

            if (discoverySession?.Devices != null)
            {
                foreach (var device in discoverySession.Devices)
                {
                    var position = device.Position switch
                    {
                        AVCaptureDevicePosition.Front => CameraPosition.Front,
                        AVCaptureDevicePosition.Back => CameraPosition.Back,
                        AVCaptureDevicePosition.Unspecified => CameraPosition.External,
                        _ => CameraPosition.Unknown
                    };

                    var camera = new CameraDevice
                    {
                        Id = device.UniqueID ?? Guid.NewGuid().ToString(),
                        Name = device.LocalizedName ?? "Unknown Camera",
                        Position = position,
                        IsAvailable = !device.Suspended
                    };

                    _detectedCameras.Add(camera);
                    _logger.LogInformation("Detected camera: {Name} ({Position}, Available: {Available})",
                        camera.Name, camera.Position, camera.IsAvailable);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting cameras");
        }
    }

    private void DetectUsbKeyboards()
    {
        try
        {
            // On macOS, we assume USB keyboards are always potentially available
            // A proper implementation would use IOKit to enumerate HID devices
            // For now, we default to true since most Macs have at least one keyboard
            _hasUsbKeyboard = true;

            _logger.LogInformation("USB keyboard detection: {HasUsb}", _hasUsbKeyboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting USB keyboards");
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
