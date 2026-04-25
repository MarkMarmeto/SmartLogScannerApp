using Microsoft.Maui.Handlers;
using SmartLog.Scanner.Controls;
using WinGrid = Microsoft.UI.Xaml.Controls.Grid;

namespace SmartLog.Scanner.Platforms.Windows;

/// <summary>
/// Windows stub handler for CameraPreviewView.
///
/// CameraPreviewView is a MacCatalyst-only concept — on Mac, camera 0's
/// AVCaptureVideoPreviewLayer is attached to it via CameraPreviewHandler.
/// Windows uses CameraQrView (one per slot) which renders its own preview,
/// so this view doesn't need to display anything. We still register a
/// handler so that MainPage.xaml's unconditional &lt;CameraPreviewView/&gt;
/// resolves cleanly on Windows instead of crashing with "Handler not found".
/// </summary>
public class CameraPreviewStubHandler : ViewHandler<CameraPreviewView, WinGrid>
{
    public static readonly IPropertyMapper<CameraPreviewView, CameraPreviewStubHandler> PropertyMapper =
        new PropertyMapper<CameraPreviewView, CameraPreviewStubHandler>(ViewHandler.ViewMapper);

    public CameraPreviewStubHandler() : base(PropertyMapper) { }

    protected override WinGrid CreatePlatformView() => new WinGrid();
}
