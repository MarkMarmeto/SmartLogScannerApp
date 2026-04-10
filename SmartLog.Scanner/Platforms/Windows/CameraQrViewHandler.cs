using Microsoft.Maui.Handlers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SmartLog.Scanner.Controls;
using WinGrid = Microsoft.UI.Xaml.Controls.Grid;

namespace SmartLog.Scanner.Platforms.Windows;

/// <summary>
/// Windows handler for CameraQrView.
/// Hosts a live camera preview (via WindowsCameraScanner + MediaCapture) inside a WinUI Grid.
/// QR codes are decoded by ZXing at ~6 fps; the full camera feed is rendered at ~15 fps.
/// USB keyboard-wedge scanners continue to work alongside the camera independently.
/// </summary>
public class CameraQrViewHandler : ViewHandler<CameraQrView, WinGrid>
{
    public static readonly IPropertyMapper<CameraQrView, CameraQrViewHandler> PropertyMapper =
        new PropertyMapper<CameraQrView, CameraQrViewHandler>(ViewHandler.ViewMapper)
        {
            [nameof(CameraQrView.IsDetecting)]     = MapIsDetecting,
            [nameof(CameraQrView.SelectedCameraId)] = MapSelectedCameraId,
        };

    private WindowsCameraScanner? _scanner;
    private Image? _previewImage;
    private WriteableBitmap? _writeableBitmap;
    private DispatcherTimer? _previewTimer;
    private DispatcherQueue? _dispatcherQueue;

    public CameraQrViewHandler() : base(PropertyMapper) { }

    // -----------------------------------------------------------------------
    // Native view lifecycle
    // -----------------------------------------------------------------------

    protected override WinGrid CreatePlatformView()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _previewImage = new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment   = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
        };

        var grid = new WinGrid();
        grid.Children.Add(_previewImage);
        return grid;
    }

    protected override void ConnectHandler(WinGrid platformView)
    {
        base.ConnectHandler(platformView);
        StartPreviewTimer();
    }

    protected override void DisconnectHandler(WinGrid platformView)
    {
        StopPreviewTimer();
        _ = StopCameraAsync();
        base.DisconnectHandler(platformView);
    }

    // -----------------------------------------------------------------------
    // Property mappers
    // -----------------------------------------------------------------------

    private static void MapIsDetecting(CameraQrViewHandler handler, CameraQrView view)
    {
        if (view.IsDetecting)
            _ = handler.StartCameraAsync(view.SelectedCameraId);
        else
            _ = handler.StopCameraAsync();
    }

    private static void MapSelectedCameraId(CameraQrViewHandler handler, CameraQrView view)
    {
        // If already scanning, restart with the newly selected camera
        if (handler._scanner?.IsScanning == true)
        {
            _ = handler.StopCameraAsync()
                .ContinueWith(_ => handler.StartCameraAsync(view.SelectedCameraId));
        }
    }

    // -----------------------------------------------------------------------
    // Camera start / stop
    // -----------------------------------------------------------------------

    private async Task StartCameraAsync(string? cameraId)
    {
        if (_scanner?.IsScanning == true) return;

        _scanner = new WindowsCameraScanner();
        _scanner.QrCodeDetected += OnQrCodeDetected;

        try
        {
            await _scanner.StartAsync(cameraId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraQrViewHandler] Camera start failed: {ex.Message}");
            _scanner.QrCodeDetected -= OnQrCodeDetected;
            _scanner.Dispose();
            _scanner = null;
        }
    }

    private async Task StopCameraAsync()
    {
        if (_scanner == null) return;
        _scanner.QrCodeDetected -= OnQrCodeDetected;
        await _scanner.StopAsync();
        _scanner.Dispose();
        _scanner = null;
    }

    // -----------------------------------------------------------------------
    // Preview rendering (~15 fps DispatcherTimer on the UI thread)
    // -----------------------------------------------------------------------

    private void StartPreviewTimer()
    {
        if (_dispatcherQueue == null) return;
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(66) };
        _previewTimer.Tick += OnPreviewTick;
        _previewTimer.Start();
    }

    private void StopPreviewTimer()
    {
        _previewTimer?.Stop();
        _previewTimer = null;
    }

    private void OnPreviewTick(object? sender, object e)
    {
        if (_scanner == null || _previewImage == null) return;

        using var frame = _scanner.TakeLatestFrame();
        if (frame == null) return;

        try
        {
            int w = frame.PixelWidth;
            int h = frame.PixelHeight;

            if (_writeableBitmap == null ||
                _writeableBitmap.PixelWidth != w ||
                _writeableBitmap.PixelHeight != h)
            {
                _writeableBitmap = new WriteableBitmap(w, h);
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

    // -----------------------------------------------------------------------
    // QR event → virtual view
    // -----------------------------------------------------------------------

    private void OnQrCodeDetected(object? sender, string value)
    {
        _dispatcherQueue?.TryEnqueue(() => VirtualView?.RaiseBarcodeDetected(value));
    }
}
