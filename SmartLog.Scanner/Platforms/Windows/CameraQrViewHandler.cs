using Microsoft.Maui.Handlers;
using SmartLog.Scanner.Controls;
using WinGrid = Microsoft.UI.Xaml.Controls.Grid;

namespace SmartLog.Scanner.Platforms.Windows;

/// <summary>
/// Windows stub handler for CameraQrView.
/// Camera-based QR scanning is not supported on Windows; USB keyboard-wedge scanners are used instead.
/// This handler renders an empty placeholder so the view inflates without throwing "Handler not found".
/// </summary>
public class CameraQrViewHandler : ViewHandler<CameraQrView, WinGrid>
{
    public static IPropertyMapper<CameraQrView, CameraQrViewHandler> PropertyMapper =
        new PropertyMapper<CameraQrView, CameraQrViewHandler>(ViewHandler.ViewMapper)
        {
            [nameof(CameraQrView.IsDetecting)] = MapIsDetecting
        };

    public CameraQrViewHandler() : base(PropertyMapper) { }

    protected override WinGrid CreatePlatformView() => new WinGrid();

    private static void MapIsDetecting(CameraQrViewHandler handler, CameraQrView view)
    {
        // No-op: camera scanning is not available on Windows.
    }
}
