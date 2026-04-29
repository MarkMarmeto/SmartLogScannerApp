using AVFoundation;
using Foundation;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>
/// EP0011: Headless Mac Catalyst camera worker.
///
/// Uses AVCaptureSession + AVCaptureMetadataOutput for QR detection.
/// Deliberately does NOT create a UIView or AVCaptureVideoPreviewLayer —
/// that was what broke the Mac Catalyst window compositor when 8 native
/// views were added to the MAUI view hierarchy simultaneously.
/// </summary>
public sealed class CameraHeadlessWorker : ICameraWorker
{
    private readonly ILogger<CameraHeadlessWorker>? _logger;
    private AVCaptureSession? _captureSession;
    private AVCaptureMetadataOutput? _metadataOutput;
    private AVCaptureVideoDataOutput? _videoDataOutput;
    private MetadataOutputDelegate? _delegate;
    private VideoOutputDelegate? _videoDelegate;
    private CoreFoundation.DispatchQueue? _videoQueue;
    private AVCaptureVideoPreviewLayer? _previewLayer;
    private bool _isRunning;

    public event EventHandler<string>? QrCodeDetected;
    public event EventHandler<string>? ErrorOccurred;
    public bool IsRunning => _isRunning;

    public CameraHeadlessWorker(ILogger<CameraHeadlessWorker>? logger = null)
    {
        _logger = logger;
    }

    public async Task StartAsync(string? deviceId = null)
    {
        if (_isRunning) return;

        _logger?.LogInformation("CameraHeadlessWorker: starting (deviceId={DeviceId})", deviceId ?? "default");

        // Camera permission
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.NotDetermined)
        {
            var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
            if (!granted)
            {
                const string msg = "Camera permission denied";
                _logger?.LogWarning(msg);
                ErrorOccurred?.Invoke(this, msg);
                return;
            }
        }
        else if (status != AVAuthorizationStatus.Authorized)
        {
            var msg = $"Camera not authorized: {status}";
            _logger?.LogWarning(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }

        _captureSession = new AVCaptureSession();

        // Device selection
        AVCaptureDevice? device = null;
        if (!string.IsNullOrWhiteSpace(deviceId))
            device = AVCaptureDevice.DeviceWithUniqueID(deviceId);
        device ??= AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);

        if (device == null)
        {
            const string msg = "No video capture device found";
            _logger?.LogError(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }

        NSError? error = null;
        var input = new AVCaptureDeviceInput(device, out error);
        if (error != null || input == null)
        {
            var msg = $"Video input error: {error?.LocalizedDescription}";
            _logger?.LogError(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }

        // Configure session inside Begin/Commit so AVFoundation re-evaluates
        // capability checks once both the input and output are attached.
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

        // QR metadata output — no preview layer, no UIView
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

        // Video data output — only added for external USB cameras. Built-in
        // cameras (FaceTime HD) work fine with just AVCaptureMetadataOutput,
        // and adding a second output to every session saturates AVFoundation
        // when multiple sessions run simultaneously, breaking QR detection on
        // the built-in camera. External cameras need it because Mac Catalyst
        // doesn't pump frames to AVCaptureMetadataOutput / AVCaptureVideoPreviewLayer
        // for ExternalUnknown devices unless a video data output is attached.
        var isExternal = device.DeviceType == AVCaptureDeviceType.ExternalUnknown;
        if (isExternal)
        {
            _videoDataOutput = new AVCaptureVideoDataOutput { AlwaysDiscardsLateVideoFrames = true };
            if (_captureSession.CanAddOutput(_videoDataOutput))
            {
                _captureSession.AddOutput(_videoDataOutput);
            }
            else
            {
                _logger?.LogWarning("CameraHeadlessWorker[{Device}]: video data output not addable", device.LocalizedName);
                _videoDataOutput.Dispose();
                _videoDataOutput = null;
            }
        }

        // Pick the highest preset the device actually supports. USB webcams
        // often don't advertise PresetHigh and StartRunning silently no-ops
        // when the preset is unsupported, leaving the preview layer black.
        var presets = new[]
        {
            AVCaptureSession.PresetHigh,
            AVCaptureSession.PresetMedium,
            AVCaptureSession.Preset640x480,
            AVCaptureSession.PresetLow,
        };
        var chosenPreset = presets.FirstOrDefault(p => _captureSession.CanSetSessionPreset(p));
        if (chosenPreset != null)
        {
            _captureSession.SessionPreset = chosenPreset;
            _logger?.LogInformation("CameraHeadlessWorker: using preset {Preset}", chosenPreset);
        }
        else
        {
            _logger?.LogWarning("CameraHeadlessWorker: no preset supported, leaving session default");
        }

        _captureSession.CommitConfiguration();

        _delegate = new MetadataOutputDelegate(this);
        _metadataOutput.SetDelegate(_delegate, CoreFoundation.DispatchQueue.MainQueue);
        _metadataOutput.MetadataObjectTypes = AVMetadataObjectType.QRCode;

        // AVCaptureVideoDataOutput requires a sample-buffer delegate on a serial
        // dispatch queue or AVFoundation skips frame delivery entirely. We don't
        // process the buffers — the delegate exists purely to keep the pipeline
        // pumping so the preview layer (and metadata output) receive frames on
        // external USB cameras under Mac Catalyst.
        if (_videoDataOutput != null)
        {
            _videoQueue = new CoreFoundation.DispatchQueue("smartlog.camera.video");
            _videoDelegate = new VideoOutputDelegate(device.LocalizedName, _logger);
            _videoDataOutput.SetSampleBufferDelegate(_videoDelegate, _videoQueue);
            _logger?.LogInformation("CameraHeadlessWorker[{Device}]: video delegate attached", device.LocalizedName);
        }
        else
        {
            _logger?.LogWarning("CameraHeadlessWorker[{Device}]: no video output — frames will not be diagnosable", device.LocalizedName);
        }

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

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        _logger?.LogInformation("CameraHeadlessWorker: stopping");

        _captureSession?.StopRunning();
        _metadataOutput?.SetDelegate(null, CoreFoundation.DispatchQueue.MainQueue);
        DetachPreview();

        _videoDataOutput?.SetSampleBufferDelegate(null, null);

        _captureSession?.Dispose();
        _metadataOutput?.Dispose();
        _videoDataOutput?.Dispose();
        _videoQueue?.Dispose();

        _captureSession = null;
        _metadataOutput = null;
        _videoDataOutput = null;
        _delegate = null;
        _videoDelegate = null;
        _videoQueue = null;
        _isRunning = false;

        _logger?.LogInformation("CameraHeadlessWorker: stopped");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    /// <summary>
    /// Attaches a live preview layer to the supplied UIView using the running capture session.
    /// Must be called after StartAsync completes. Safe to call multiple times — replaces the previous layer.
    /// </summary>
    public void AttachPreview(UIKit.UIView containerView)
    {
        if (_captureSession == null) return;

        // Remove existing preview layer if any
        DetachPreview();

        _previewLayer = AVCaptureVideoPreviewLayer.FromSession(_captureSession);
        _previewLayer.VideoGravity = AVLayerVideoGravity.ResizeAspectFill;
        _previewLayer.Frame = containerView.Bounds;

        containerView.Layer.AddSublayer(_previewLayer);
        _logger?.LogDebug("CameraHeadlessWorker: preview layer attached");
    }

    /// <summary>
    /// Removes the preview layer from its superlayer and releases it.
    /// The capture session continues running headlessly.
    /// </summary>
    public void DetachPreview()
    {
        if (_previewLayer == null) return;
        _previewLayer.RemoveFromSuperLayer();
        _previewLayer.Dispose();
        _previewLayer = null;
        _logger?.LogDebug("CameraHeadlessWorker: preview layer detached");
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

    // Sample-buffer delegate. Mac Catalyst silently skips frame delivery for
    // AVCaptureVideoDataOutput unless a delegate is set on a queue, so this
    // exists primarily to force the capture pipeline to flow. We also log
    // frame counts so we can confirm whether external USB cameras are actually
    // producing frames under Mac Catalyst.
    private sealed class VideoOutputDelegate : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        private readonly string _deviceName;
        private readonly ILogger<CameraHeadlessWorker>? _logger;
        private int _frames;

        public VideoOutputDelegate(string deviceName, ILogger<CameraHeadlessWorker>? logger)
        {
            _deviceName = deviceName;
            _logger = logger;
        }

        public override void DidOutputSampleBuffer(
            AVCaptureOutput captureOutput,
            CoreMedia.CMSampleBuffer sampleBuffer,
            AVCaptureConnection connection)
        {
            var n = System.Threading.Interlocked.Increment(ref _frames);
            if (n == 1)
                _logger?.LogInformation("CameraHeadlessWorker[{Device}]: FIRST frame received", _deviceName);
            else if (n % 30 == 0)
                _logger?.LogInformation("CameraHeadlessWorker[{Device}]: {Frames} frames received", _deviceName, n);
            sampleBuffer.Dispose();
        }
    }
}
