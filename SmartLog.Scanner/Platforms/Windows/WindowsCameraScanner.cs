using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using ZXing;
using ZXing.Common;

namespace SmartLog.Scanner.Platforms.Windows;

/// <summary>
/// Windows camera QR scanner using MediaCapture + MediaFrameReader for live preview
/// and ZXing.Net for QR decoding.
///
/// Preview frames are written to <see cref="LatestFrame"/> every frame;
/// call <see cref="TakeLatestFrame"/> to dequeue and own the SoftwareBitmap.
/// QR decode is throttled to every <see cref="QrDecodeEveryNFrames"/> frames.
/// </summary>
public sealed class WindowsCameraScanner : IDisposable
{
    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private readonly BarcodeReader _barcodeReader;

    private volatile bool _isScanning;
    private int _frameCount;
    private SoftwareBitmap? _latestFrame;

    // Decode QR at ~6 fps assuming a 30 fps camera feed
    private const int QrDecodeEveryNFrames = 5;

    public event EventHandler<string>? QrCodeDetected;

    public WindowsCameraScanner()
    {
        _barcodeReader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingHints
            {
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                TryHarder = true
            }
        };
    }

    public bool IsScanning => _isScanning;

    /// <summary>
    /// Starts the camera and frame reader using the specified device ID.
    /// Pass null or empty string to let Windows choose the default camera.
    /// </summary>
    public async Task StartAsync(string? deviceId = null)
    {
        if (_isScanning) return;

        _mediaCapture = new MediaCapture();

        var settings = string.IsNullOrWhiteSpace(deviceId)
            ? new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            }
            : new MediaCaptureInitializationSettings
            {
                VideoDeviceId = deviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            };

        await _mediaCapture.InitializeAsync(settings);

        // Prefer VideoPreview stream; fall back to VideoRecord
        var frameSource =
            _mediaCapture.FrameSources.Values.FirstOrDefault(
                s => s.Info.MediaStreamType == MediaStreamType.VideoPreview)
            ?? _mediaCapture.FrameSources.Values.FirstOrDefault(
                s => s.Info.MediaStreamType == MediaStreamType.VideoRecord)
            ?? _mediaCapture.FrameSources.Values.FirstOrDefault();

        if (frameSource == null)
            throw new InvalidOperationException("No video frame source found on the selected camera.");

        _frameReader = await _mediaCapture.CreateFrameReaderAsync(
            frameSource,
            MediaEncodingSubtypes.Bgra8);

        _frameReader.FrameArrived += OnFrameArrived;
        _frameCount = 0;
        _isScanning = true;

        await _frameReader.StartAsync();
    }

    public async Task StopAsync()
    {
        if (!_isScanning) return;
        _isScanning = false;

        if (_frameReader != null)
        {
            _frameReader.FrameArrived -= OnFrameArrived;
            await _frameReader.StopAsync();
            _frameReader.Dispose();
            _frameReader = null;
        }

        _mediaCapture?.Dispose();
        _mediaCapture = null;

        // Discard any pending preview frame
        var stale = Interlocked.Exchange(ref _latestFrame, null);
        stale?.Dispose();
    }

    /// <summary>
    /// Returns the most recent preview frame and removes it from the internal slot.
    /// The caller MUST dispose the returned bitmap after use.
    /// Returns null when no new frame has arrived since the last call.
    /// </summary>
    public SoftwareBitmap? TakeLatestFrame() =>
        Interlocked.Exchange(ref _latestFrame, null);

    // -----------------------------------------------------------------------
    // Frame handler (runs on MediaCapture background thread)
    // -----------------------------------------------------------------------

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!_isScanning) return;

        using var frame = sender.TryAcquireLatestFrame();
        if (frame?.VideoMediaFrame?.SoftwareBitmap == null) return;

        var source = frame.VideoMediaFrame.SoftwareBitmap;

        // ------------------------------------------------------------------
        // 1. Convert to BGRA8 for both preview rendering and ZXing decoding
        // ------------------------------------------------------------------
        SoftwareBitmap? bgra;
        try
        {
            bgra = source.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? SoftwareBitmap.Copy(source)
                : SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }
        catch
        {
            return;
        }

        // ------------------------------------------------------------------
        // 2. Swap into preview slot (dispose the frame the UI hasn't consumed)
        // ------------------------------------------------------------------
        var old = Interlocked.Exchange(ref _latestFrame, bgra);
        old?.Dispose();

        // ------------------------------------------------------------------
        // 3. QR decode on every Nth frame (throttled)
        // ------------------------------------------------------------------
        var count = Interlocked.Increment(ref _frameCount);
        if (count % QrDecodeEveryNFrames != 0) return;

        try
        {
            int w = bgra.PixelWidth;
            int h = bgra.PixelHeight;
            byte[] pixels = new byte[w * h * 4];
            bgra.CopyToBuffer(pixels.AsBuffer());

            var luminance = new RGBLuminanceSource(
                pixels, w, h,
                RGBLuminanceSource.BitmapFormat.BGRA32);

            var result = _barcodeReader.Decode(luminance);
            if (!string.IsNullOrEmpty(result?.Text))
                QrCodeDetected?.Invoke(this, result.Text);
        }
        catch
        {
            // Swallow decode errors — bad frames are normal
        }
    }

    public void Dispose()
    {
        if (_isScanning)
            _ = StopAsync();
    }
}
