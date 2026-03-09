using AVFoundation;
using CoreGraphics;
using CoreVideo;
using Foundation;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using UIKit;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>
/// Custom camera view that uses AVFoundation to detect and decode QR codes.
/// </summary>
public class CameraQrScannerView : UIView
{
    private readonly ILogger<CameraQrScannerView>? _logger;
    private AVCaptureSession? _captureSession;
    private AVCaptureVideoPreviewLayer? _previewLayer;
    private AVCaptureMetadataOutput? _metadataOutput;
    private bool _isScanning;

    public event EventHandler<string>? QrCodeDetected;

    public CameraQrScannerView(ILogger<CameraQrScannerView>? logger = null)
    {
        _logger = logger;
        BackgroundColor = UIColor.Black;
    }

    public async Task StartScanningAsync()
    {
        if (_isScanning)
            return;

        _logger?.LogInformation("Starting camera QR scanning...");

        // Request camera permission
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.NotDetermined)
        {
            var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
            if (!granted)
            {
                _logger?.LogWarning("Camera permission denied");
                return;
            }
        }
        else if (status != AVAuthorizationStatus.Authorized)
        {
            _logger?.LogWarning("Camera not authorized: {Status}", status);
            return;
        }

        // Initialize capture session
        _captureSession = new AVCaptureSession();
        _captureSession.SessionPreset = AVCaptureSession.PresetHigh;

        // Get default video device
        var videoDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
        if (videoDevice == null)
        {
            _logger?.LogError("No video capture device found");
            return;
        }

        // Create input
        NSError? error = null;
        var videoInput = new AVCaptureDeviceInput(videoDevice, out error);
        if (error != null || videoInput == null)
        {
            _logger?.LogError("Error creating video input: {Error}", error?.LocalizedDescription);
            return;
        }

        if (_captureSession.CanAddInput(videoInput))
        {
            _captureSession.AddInput(videoInput);
        }
        else
        {
            _logger?.LogError("Cannot add video input to capture session");
            return;
        }

        // Create metadata output for QR code detection
        _metadataOutput = new AVCaptureMetadataOutput();
        if (_captureSession.CanAddOutput(_metadataOutput))
        {
            _captureSession.AddOutput(_metadataOutput);

            // Set delegate to receive QR code detections
            var metadataDelegate = new MetadataOutputDelegate(this);
            _metadataOutput.SetDelegate(metadataDelegate, CoreFoundation.DispatchQueue.MainQueue);

            // Specify QR code as the metadata type
            _metadataOutput.MetadataObjectTypes = AVMetadataObjectType.QRCode;
        }
        else
        {
            _logger?.LogError("Cannot add metadata output to capture session");
            return;
        }

        // Create preview layer
        _previewLayer = new AVCaptureVideoPreviewLayer(_captureSession)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = Bounds
        };

        Layer.AddSublayer(_previewLayer);

        // Start the capture session
        await Task.Run(() => _captureSession.StartRunning());
        _isScanning = true;

        _logger?.LogInformation("Camera QR scanning started successfully");
    }

    public void StopScanning()
    {
        if (!_isScanning)
            return;

        _logger?.LogInformation("Stopping camera QR scanning...");

        _captureSession?.StopRunning();
        _previewLayer?.RemoveFromSuperLayer();

        _captureSession?.Dispose();
        _previewLayer?.Dispose();
        _metadataOutput?.Dispose();

        _captureSession = null;
        _previewLayer = null;
        _metadataOutput = null;
        _isScanning = false;

        _logger?.LogInformation("Camera QR scanning stopped");
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        if (_previewLayer != null)
        {
            _previewLayer.Frame = Bounds;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopScanning();
        }
        base.Dispose(disposing);
    }

    private void OnQrCodeDetected(string value)
    {
        _logger?.LogInformation("QR code detected in native view: {Value}", value);
        System.Diagnostics.Debug.WriteLine($"[CameraQrScannerView] QR code detected: {value}");

        // DEBUGGING: Flash the screen briefly to show detection is working
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BackgroundColor = UIKit.UIColor.Green;
            Task.Delay(100).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    BackgroundColor = UIKit.UIColor.Black;
                });
            });
        });

        QrCodeDetected?.Invoke(this, value);
    }

    /// <summary>
    /// Delegate for handling metadata output (QR code detections).
    /// </summary>
    private class MetadataOutputDelegate : AVCaptureMetadataOutputObjectsDelegate
    {
        private readonly CameraQrScannerView _parent;

        public MetadataOutputDelegate(CameraQrScannerView parent)
        {
            _parent = parent;
        }

        public override void DidOutputMetadataObjects(AVCaptureMetadataOutput captureOutput,
            AVMetadataObject[] metadataObjects, AVCaptureConnection connection)
        {
            System.Diagnostics.Debug.WriteLine($"[MetadataDelegate] DidOutputMetadataObjects called with {metadataObjects?.Length ?? 0} objects");

            if (metadataObjects == null || metadataObjects.Length == 0)
                return;

            foreach (var obj in metadataObjects)
            {
                System.Diagnostics.Debug.WriteLine($"[MetadataDelegate] Found object type: {obj.Type}");
            }

            // Get first QR code detected
            var metadataObject = metadataObjects.FirstOrDefault(obj =>
                obj.Type == AVMetadataObjectType.QRCode);

            if (metadataObject != null)
            {
                System.Diagnostics.Debug.WriteLine("[MetadataDelegate] Found QR code object");
            }

            if (metadataObject is AVMetadataMachineReadableCodeObject readableObject
                && !string.IsNullOrEmpty(readableObject.StringValue))
            {
                System.Diagnostics.Debug.WriteLine($"[MetadataDelegate] QR code value: {readableObject.StringValue}");
                _parent.OnQrCodeDetected(readableObject.StringValue);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[MetadataDelegate] QR code object has no readable value");
            }
        }
    }
}
