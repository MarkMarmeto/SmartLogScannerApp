using Microsoft.Maui.Handlers;
using UIKit;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>
/// EP0011: Platform handler for CameraPreviewView on Mac Catalyst.
///
/// Creates a plain UIView that serves as the host for the AVCaptureVideoPreviewLayer
/// owned by CameraHeadlessWorker. Call AttachWorkerPreview() after cameras are started
/// to wire the preview layer into this view. The layer is owned by the worker and
/// is detached/disposed when the worker stops — this handler just provides the container.
/// </summary>
public class CameraPreviewHandler : ViewHandler<Controls.CameraPreviewView, UIView>
{
    public static readonly PropertyMapper<Controls.CameraPreviewView, CameraPreviewHandler>
        Mapper = new(ViewMapper);

    public CameraPreviewHandler() : base(Mapper) { }

    protected override UIView CreatePlatformView() => new UIView
    {
        BackgroundColor = UIColor.Black,
        ClipsToBounds = true
    };

    /// <summary>
    /// Attaches the worker's AVCaptureVideoPreviewLayer to this handler's UIView.
    /// The worker owns the layer lifetime — this just provides the container view.
    /// Must be called on the main thread, after the worker has started its capture session.
    /// </summary>
    public void AttachWorkerPreview(CameraHeadlessWorker worker)
    {
        worker.AttachPreview(PlatformView);
    }

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);

        // Keep preview layer frame in sync when the view is laid out
        platformView.LayoutSubviews();
    }
}
