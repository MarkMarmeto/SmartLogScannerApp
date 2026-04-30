using AVFoundation;
using CoreFoundation;
using CoreImage;
using CoreMedia;
using CoreVideo;
using Foundation;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>
/// EP0011: Headless Mac Catalyst camera worker.
///
/// Two execution paths:
///
/// 1. Multi-cam path (preferred). When the OS reports
///    <see cref="AVCaptureMultiCamSession.MultiCamSupported"/> = true and a
///    <see cref="MacMultiCamSessionHost"/> singleton is injected, this worker registers
///    its input + a per-camera metadata output on the shared session. This is the only
///    way to run two cameras concurrently on Mac Catalyst — parallel
///    <see cref="AVCaptureSession"/> instances starve each other.
///
/// 2. Single-session fallback (older Intel Macs that don't expose multi-cam support).
///    Each worker owns its own session. Multi-camera operation is unreliable on this
///    path; it exists so single-camera scanning still works on unsupported hardware.
///
/// Deliberately does NOT create a UIView in the fallback path either — the preview
/// layer is attached to a UIView supplied by <see cref="CameraPreviewHandler"/> only
/// for the first enabled slot.
/// </summary>
public sealed class CameraHeadlessWorker : ICameraWorker
{
    private readonly ILogger<CameraHeadlessWorker>? _logger;
    private readonly MacMultiCamSessionHost? _multiCamHost;

    // Multi-cam path state
    private MacMultiCamRegistration? _registration;
    private MacMultiCamPreviewHandle? _multiCamPreview;
    private VideoSampleDelegate? _videoDelegate;
    private DispatchQueue? _videoQueue;

    // Single-session fallback state
    private AVCaptureSession? _captureSession;
    private AVCaptureMetadataOutput? _metadataOutput;
    private AVCaptureVideoPreviewLayer? _previewLayer;

    private MetadataOutputDelegate? _delegate;
    private bool _isRunning;

    public event EventHandler<string>? QrCodeDetected;
    public event EventHandler<string>? ErrorOccurred;
    public bool IsRunning => _isRunning;

    public CameraHeadlessWorker(
        ILogger<CameraHeadlessWorker>? logger = null,
        MacMultiCamSessionHost? multiCamHost = null)
    {
        _logger = logger;
        _multiCamHost = multiCamHost;
    }

    private bool UseMultiCam => _multiCamHost != null && AVCaptureMultiCamSession.MultiCamSupported;

    public async Task StartAsync(string? deviceId = null)
    {
        if (_isRunning) return;

        _logger?.LogInformation("CameraHeadlessWorker: starting (deviceId={DeviceId})", deviceId ?? "default");

        if (!await EnsureCameraPermissionAsync())
            return;

        var device = ResolveDevice(deviceId);
        if (device == null)
        {
            const string msg = "No video capture device found";
            _logger?.LogError(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }

        if (UseMultiCam)
        {
            await StartMultiCamAsync(device);
        }
        else
        {
            await StartSingleSessionAsync(device);
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        _logger?.LogInformation("CameraHeadlessWorker: stopping");

        DetachPreview();

        if (_registration != null && _multiCamHost != null)
        {
            try { _registration.VideoOutput.SetSampleBufferDelegate(null, null); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Multi-cam: clearing sample buffer delegate threw"); }

            _multiCamHost.Unregister(_registration);
            _registration = null;

            _videoDelegate?.Dispose();
            _videoDelegate = null;
            _videoQueue?.Dispose();
            _videoQueue = null;
        }
        else
        {
            _captureSession?.StopRunning();
            _metadataOutput?.SetDelegate(null, CoreFoundation.DispatchQueue.MainQueue);

            _captureSession?.Dispose();
            _metadataOutput?.Dispose();

            _captureSession = null;
            _metadataOutput = null;
        }

        _delegate = null;
        _isRunning = false;

        _logger?.LogInformation("CameraHeadlessWorker: stopped");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    /// <summary>
    /// Attaches a live preview layer to the supplied UIView. Must be called after
    /// <see cref="StartAsync"/>. Safe to call multiple times — replaces the previous layer.
    /// </summary>
    public void AttachPreview(UIKit.UIView containerView)
    {
        DetachPreview();

        if (_registration != null && _multiCamHost != null)
        {
            _multiCamPreview = _multiCamHost.AttachPreview(_registration, containerView);
            if (_multiCamPreview != null)
                _logger?.LogDebug("CameraHeadlessWorker: multi-cam preview attached");
            else
                _logger?.LogWarning("CameraHeadlessWorker: multi-cam preview attach returned null");
            return;
        }

        if (_captureSession == null) return;

        _previewLayer = AVCaptureVideoPreviewLayer.FromSession(_captureSession);
        _previewLayer.VideoGravity = AVLayerVideoGravity.ResizeAspectFill;
        _previewLayer.Frame = containerView.Bounds;
        containerView.Layer.AddSublayer(_previewLayer);
        _logger?.LogDebug("CameraHeadlessWorker: single-session preview attached");
    }

    /// <summary>
    /// Removes the preview layer. The capture session continues running headlessly.
    /// </summary>
    public void DetachPreview()
    {
        if (_multiCamPreview != null && _multiCamHost != null)
        {
            _multiCamHost.DetachPreview(_multiCamPreview);
            _multiCamPreview = null;
            return;
        }

        if (_previewLayer == null) return;
        _previewLayer.RemoveFromSuperLayer();
        _previewLayer.Dispose();
        _previewLayer = null;
    }

    private static async Task<bool> EnsureCameraPermissionAsync()
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.NotDetermined)
        {
            return await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
        }
        return status == AVAuthorizationStatus.Authorized;
    }

    private static AVCaptureDevice? ResolveDevice(string? deviceId)
    {
        AVCaptureDevice? device = null;
        if (!string.IsNullOrWhiteSpace(deviceId))
            device = AVCaptureDevice.DeviceWithUniqueID(deviceId);
        return device ?? AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
    }

    private async Task StartMultiCamAsync(AVCaptureDevice device)
    {
        var registration = await Task.Run(() => _multiCamHost!.Register(device));
        if (registration == null)
        {
            var msg = $"Multi-cam registration failed for {device.LocalizedName}";
            _logger?.LogError(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }

        _registration = registration;
        _videoQueue = new DispatchQueue($"smartlog.camera.{device.UniqueID}");
        _videoDelegate = new VideoSampleDelegate(this);
        registration.VideoOutput.SetSampleBufferDelegate(_videoDelegate, _videoQueue);

        _isRunning = true;
        _logger?.LogInformation("CameraHeadlessWorker: running ({Device})", device.LocalizedName);
    }

    private async Task StartSingleSessionAsync(AVCaptureDevice device)
    {
        _captureSession = new AVCaptureSession();

        NSError? error = null;
        var input = new AVCaptureDeviceInput(device, out error);
        if (error != null || input == null)
        {
            var msg = $"Video input error: {error?.LocalizedDescription}";
            _logger?.LogError(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }

        _captureSession.BeginConfiguration();

        if (!_captureSession.CanAddInput(input))
        {
            _captureSession.CommitConfiguration();
            const string msg = "Cannot add video input to capture session";
            _logger?.LogError(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }
        _captureSession.AddInput(input);

        _metadataOutput = new AVCaptureMetadataOutput();
        if (!_captureSession.CanAddOutput(_metadataOutput))
        {
            _captureSession.CommitConfiguration();
            const string msg = "Cannot add metadata output to capture session";
            _logger?.LogError(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }
        _captureSession.AddOutput(_metadataOutput);

        // Pick the highest preset the device actually supports. USB webcams often don't
        // advertise PresetHigh and StartRunning silently no-ops when the preset is unsupported.
        var presets = new[]
        {
            AVCaptureSession.PresetHigh,
            AVCaptureSession.PresetMedium,
            AVCaptureSession.Preset640x480,
            AVCaptureSession.PresetLow,
        };
        var chosenPreset = presets.FirstOrDefault(p => _captureSession.CanSetSessionPreset(p));
        if (chosenPreset != null)
            _captureSession.SessionPreset = chosenPreset;

        _captureSession.CommitConfiguration();

        _delegate = new MetadataOutputDelegate(this);
        _metadataOutput.SetDelegate(_delegate, CoreFoundation.DispatchQueue.MainQueue);
        _metadataOutput.MetadataObjectTypes = AVMetadataObjectType.QRCode;

        await Task.Run(() => _captureSession.StartRunning());

        if (!_captureSession.Running)
        {
            var msg = $"Capture session failed to start (device {device.LocalizedName})";
            _logger?.LogError(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }

        _isRunning = true;
        _logger?.LogInformation("CameraHeadlessWorker: running ({Device})", device.LocalizedName);
    }

    private void OnQrCodeDetected(string value)
    {
        _logger?.LogDebug("CameraHeadlessWorker: QR detected");
        QrCodeDetected?.Invoke(this, value);
    }

    private sealed class MetadataOutputDelegate : AVCaptureMetadataOutputObjectsDelegate
    {
        private readonly CameraHeadlessWorker _parent;

        public MetadataOutputDelegate(CameraHeadlessWorker parent)
        {
            _parent = parent;
        }

        public override void DidOutputMetadataObjects(
            AVCaptureMetadataOutput captureOutput,
            AVMetadataObject[] metadataObjects,
            AVCaptureConnection connection)
        {
            if (metadataObjects == null || metadataObjects.Length == 0) return;

            var qr = metadataObjects.FirstOrDefault(o => o.Type == AVMetadataObjectType.QRCode);
            if (qr is AVMetadataMachineReadableCodeObject readable
                && !string.IsNullOrEmpty(readable.StringValue))
            {
                _parent.OnQrCodeDetected(readable.StringValue);
            }
        }
    }

    /// <summary>
    /// Multi-cam path: AVCaptureMultiCamSession does not support AVCaptureMetadataOutput,
    /// so QR codes are decoded from raw frames. CIDetector with the QRCode feature is
    /// CPU-cheap on Apple Silicon. Decodes every Nth frame (matches the Windows path's
    /// 5-frame throttle, ~6 fps from a 30 fps camera) to avoid CPU/thermal pressure when
    /// running multiple cameras concurrently.
    /// </summary>
    private sealed class VideoSampleDelegate : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        private const int DecodeEveryNFrames = 5;
        private readonly CameraHeadlessWorker _parent;
        private readonly CIDetector _detector;
        private int _frameCounter;

        public VideoSampleDelegate(CameraHeadlessWorker parent)
        {
            _parent = parent;
            // CIDetector accepts a nullable CIContext (Apple uses a default), but the .NET
            // binding is non-nullable; null-forgiving keeps the call concise.
            _detector = CIDetector.CreateQRDetector(
                context: null!,
                detectorOptions: new CIDetectorOptions { Accuracy = FaceDetectorAccuracy.High })!;
        }

        public override void DidOutputSampleBuffer(
            AVCaptureOutput captureOutput,
            CMSampleBuffer sampleBuffer,
            AVCaptureConnection connection)
        {
            try
            {
                if ((Interlocked.Increment(ref _frameCounter) % DecodeEveryNFrames) != 0)
                    return;

                using var pixelBuffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
                if (pixelBuffer == null) return;

                using var ciImage = CIImage.FromImageBuffer(pixelBuffer);
                var features = _detector.FeaturesInImage(ciImage);
                if (features == null) return;

                foreach (var f in features)
                {
                    if (f is CIQRCodeFeature qr && !string.IsNullOrEmpty(qr.MessageString))
                    {
                        _parent.OnQrCodeDetected(qr.MessageString);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _parent._logger?.LogWarning(ex, "Multi-cam: QR decode threw");
            }
            finally
            {
                sampleBuffer.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _detector.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
