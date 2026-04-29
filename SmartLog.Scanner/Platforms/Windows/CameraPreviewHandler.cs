using Microsoft.Maui.Handlers;
using Microsoft.UI.Dispatching;
using SmartLog.Scanner.Controls;
using WinGrid = Microsoft.UI.Xaml.Controls.Grid;
using WinImage = Microsoft.UI.Xaml.Controls.Image;

namespace SmartLog.Scanner.Platforms.Windows;

/// <summary>
/// Windows handler for CameraPreviewView.
///
/// Mac's equivalent attaches an AVCaptureVideoPreviewLayer owned by the
/// CameraHeadlessWorker. Windows can't do that (MediaCapture has no analogous
/// shared preview layer), so this handler runs a ~15 fps DispatcherQueueTimer
/// that pulls SoftwareBitmaps from the attached worker and renders them into
/// a WriteableBitmap-backed Image. MainPage calls AttachWorker(worker) after
/// the multi-camera manager has started, just like the Mac path.
/// </summary>
public class CameraPreviewHandler : ViewHandler<CameraPreviewView, WinGrid>
{
    public static readonly IPropertyMapper<CameraPreviewView, CameraPreviewHandler> PropertyMapper =
        new PropertyMapper<CameraPreviewView, CameraPreviewHandler>(ViewHandler.ViewMapper);

    private CameraHeadlessWorker? _worker;
    private WinImage? _previewImage;
    private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? _writeableBitmap;
    private DispatcherQueueTimer? _previewTimer;
    private DispatcherQueue? _dispatcherQueue;

    public CameraPreviewHandler() : base(PropertyMapper) { }

    protected override WinGrid CreatePlatformView()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _previewImage = new WinImage
        {
            Stretch             = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment   = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
        };

        var grid = new WinGrid();
        grid.Children.Add(_previewImage);
        return grid;
    }

    /// <summary>
    /// Wires the preview to a worker. Idempotent; replaces any previously
    /// attached worker. Starts the render timer if not already running.
    /// </summary>
    public void AttachWorker(CameraHeadlessWorker worker)
    {
        _worker = worker;
        StartPreviewTimer();
    }

    protected override void DisconnectHandler(WinGrid platformView)
    {
        StopPreviewTimer();
        _worker = null;
        base.DisconnectHandler(platformView);
    }

    private void StartPreviewTimer()
    {
        if (_dispatcherQueue == null || _previewTimer != null) return;
        _previewTimer = _dispatcherQueue.CreateTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(66);
        _previewTimer.Tick += OnPreviewTick;
        _previewTimer.Start();
    }

    private void StopPreviewTimer()
    {
        _previewTimer?.Stop();
        _previewTimer = null;
    }

    private void OnPreviewTick(DispatcherQueueTimer sender, object args)
    {
        if (_worker == null || _previewImage == null) return;

        using var frame = _worker.TakeLatestFrame();
        if (frame == null) return;

        try
        {
            int w = frame.PixelWidth;
            int h = frame.PixelHeight;

            if (_writeableBitmap == null ||
                _writeableBitmap.PixelWidth  != w ||
                _writeableBitmap.PixelHeight != h)
            {
                _writeableBitmap = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(w, h);
            }

            frame.CopyToBuffer(_writeableBitmap.PixelBuffer);
            _writeableBitmap.Invalidate();
            _previewImage.Source = _writeableBitmap;
        }
        catch
        {
            // Swallow rendering errors from bad frames
        }
    }
}
