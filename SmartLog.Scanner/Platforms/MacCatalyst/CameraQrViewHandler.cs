using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using SmartLog.Scanner.Controls;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>
/// MacCatalyst handler for CameraQrView.
/// Maps the cross-platform control to the native AVFoundation implementation.
/// </summary>
public class CameraQrViewHandler : ViewHandler<CameraQrView, CameraQrScannerView>
{
    private readonly ILogger<CameraQrScannerView>? _logger;

    public CameraQrViewHandler(ILogger<CameraQrScannerView>? logger = null) : base(PropertyMapper)
    {
        _logger = logger;
    }

    public static IPropertyMapper<CameraQrView, CameraQrViewHandler> PropertyMapper = new PropertyMapper<CameraQrView, CameraQrViewHandler>(ViewHandler.ViewMapper)
    {
        [nameof(CameraQrView.IsDetecting)]     = MapIsDetecting,
        [nameof(CameraQrView.SelectedCameraId)] = MapSelectedCameraId,
    };

    protected override CameraQrScannerView CreatePlatformView()
    {
        var nativeView = new CameraQrScannerView(_logger);
        nativeView.QrCodeDetected += OnQrCodeDetected;
        return nativeView;
    }

    protected override void DisconnectHandler(CameraQrScannerView platformView)
    {
        platformView.QrCodeDetected -= OnQrCodeDetected;
        platformView.StopScanning();
        base.DisconnectHandler(platformView);
    }

    private static void MapIsDetecting(CameraQrViewHandler handler, CameraQrView view)
    {
        if (view.IsDetecting)
            _ = handler.PlatformView.StartScanningAsync(view.SelectedCameraId);
        else
            handler.PlatformView.StopScanning();
    }

    private static void MapSelectedCameraId(CameraQrViewHandler handler, CameraQrView view)
    {
        // Restart with the new device if currently scanning
        if (handler.PlatformView != null)
        {
            handler.PlatformView.StopScanning();
            if (view.IsDetecting)
                _ = handler.PlatformView.StartScanningAsync(view.SelectedCameraId);
        }
    }

    private void OnQrCodeDetected(object? sender, string value)
    {
        if (VirtualView != null)
        {
            VirtualView.RaiseBarcodeDetected(value);
        }
    }
}
