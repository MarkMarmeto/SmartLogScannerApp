namespace SmartLog.Scanner.Controls;

/// <summary>
/// EP0011: Lightweight host view for the camera 0 live preview.
///
/// No QR detection here — QR capture is handled entirely by CameraHeadlessWorker.
/// The platform handler (MacCatalyst: CameraPreviewHandler) creates a native UIView
/// and the worker's AVCaptureVideoPreviewLayer is attached to it after cameras start.
///
/// One instance per app; shown only in Camera mode, hidden in USB mode.
/// </summary>
public class CameraPreviewView : View
{
}
