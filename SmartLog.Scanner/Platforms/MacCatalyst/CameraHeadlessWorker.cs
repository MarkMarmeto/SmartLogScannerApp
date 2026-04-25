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
    private MetadataOutputDelegate? _delegate;
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
        _captureSession.SessionPreset = AVCaptureSession.PresetHigh;

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

        if (!_captureSession.CanAddInput(input))
        {
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
            const string msg = "Cannot add metadata output to capture session";
            _logger?.LogError(msg);
            ErrorOccurred?.Invoke(this, msg);
            return;
        }
        _captureSession.AddOutput(_metadataOutput);

        _delegate = new MetadataOutputDelegate(this);
        _metadataOutput.SetDelegate(_delegate, CoreFoundation.DispatchQueue.MainQueue);
        _metadataOutput.MetadataObjectTypes = AVMetadataObjectType.QRCode;

        await Task.Run(() => _captureSession.StartRunning());
        _isRunning = true;

        _logger?.LogInformation("CameraHeadlessWorker: running");
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        _logger?.LogInformation("CameraHeadlessWorker: stopping");

        _captureSession?.StopRunning();
        _metadataOutput?.SetDelegate(null, CoreFoundation.DispatchQueue.MainQueue);
        DetachPreview();

        _captureSession?.Dispose();
        _metadataOutput?.Dispose();

        _captureSession = null;
        _metadataOutput = null;
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
}
