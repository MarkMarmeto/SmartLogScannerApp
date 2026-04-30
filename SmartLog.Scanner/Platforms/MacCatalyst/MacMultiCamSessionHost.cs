using AVFoundation;
using CoreFoundation;
using CoreVideo;
using Foundation;
using Microsoft.Extensions.Logging;
using UIKit;

namespace SmartLog.Scanner.Platforms.MacCatalyst;

/// <summary>
/// EP0011 macOS multi-camera support. Owns a single <see cref="AVCaptureMultiCamSession"/>
/// shared by every <see cref="CameraHeadlessWorker"/> on Mac Catalyst.
///
/// Mac Catalyst cannot deliver frames to two parallel <see cref="AVCaptureSession"/> instances.
/// The capability spike confirmed <c>AVCaptureMultiCamSession.MultiCamSupported = true</c> and
/// every enumerated format reports <c>MultiCamSupported = true</c>, so a single shared session
/// with manual per-camera connections is the supported route.
///
/// Lifetime: registered as a DI singleton; first registration starts the session, last
/// unregistration stops it.
/// </summary>
public sealed class MacMultiCamSessionHost : IDisposable
{
    private readonly ILogger<MacMultiCamSessionHost>? _logger;
    private readonly object _gate = new();
    private readonly List<MacMultiCamRegistration> _registrations = new();
    private AVCaptureMultiCamSession? _session;
    private bool _disposed;

    public MacMultiCamSessionHost(ILogger<MacMultiCamSessionHost>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds <paramref name="device"/> as a new input on the shared session, attaches a
    /// dedicated <see cref="AVCaptureMetadataOutput"/>, and wires the input's video port
    /// to that output. The first registration starts the session.
    /// </summary>
    /// <returns>A registration handle, or null if the registration failed.</returns>
    public MacMultiCamRegistration? Register(AVCaptureDevice device)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            _session ??= new AVCaptureMultiCamSession();

            NSError? error = null;
            var input = new AVCaptureDeviceInput(device, out error);
            if (error != null || input == null)
            {
                _logger?.LogError("Multi-cam: input creation failed for {Device}: {Error}",
                    device.LocalizedName, error?.LocalizedDescription);
                return null;
            }

            // Pick a multi-cam-compatible format closest to 1280x720. The probe showed every
            // format on every device has MultiCamSupported=true, but we still filter defensively
            // and bail if the device exposes none.
            var format = SelectFormat(device);
            if (format == null)
            {
                _logger?.LogError("Multi-cam: no MultiCamSupported format on {Device}", device.LocalizedName);
                input.Dispose();
                return null;
            }

            try
            {
                if (device.LockForConfiguration(out var lockError))
                {
                    device.ActiveFormat = format;
                    device.UnlockForConfiguration();
                }
                else
                {
                    _logger?.LogWarning("Multi-cam: LockForConfiguration failed on {Device}: {Error}",
                        device.LocalizedName, lockError?.LocalizedDescription);
                    // Continue anyway — session may still negotiate a default format.
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Multi-cam: format application threw on {Device}", device.LocalizedName);
            }

            // AVCaptureMultiCamSession does not support AVCaptureMetadataOutput
            // (Apple's "AVMultiCamPiP" sample + multi-cam programming guide). Use a
            // per-camera AVCaptureVideoDataOutput instead; the worker decodes QR codes
            // from the sample buffer using CIDetector.
            var videoOutput = new AVCaptureVideoDataOutput
            {
                AlwaysDiscardsLateVideoFrames = true,
                WeakVideoSettings = new NSDictionary(
                    CVPixelBuffer.PixelFormatTypeKey,
                    new NSNumber((int)CVPixelFormatType.CV32BGRA)),
            };

            _session.BeginConfiguration();
            try
            {
                if (!_session.CanAddInput(input))
                {
                    _logger?.LogError("Multi-cam: session cannot add input for {Device}", device.LocalizedName);
                    _session.CommitConfiguration();
                    videoOutput.Dispose();
                    input.Dispose();
                    return null;
                }
                _session.AddInputWithNoConnections(input);

                if (!_session.CanAddOutput(videoOutput))
                {
                    _logger?.LogError("Multi-cam: session cannot add video data output for {Device}", device.LocalizedName);
                    _session.RemoveInput(input);
                    _session.CommitConfiguration();
                    videoOutput.Dispose();
                    input.Dispose();
                    return null;
                }
                _session.AddOutputWithNoConnections(videoOutput);

                // A video AVCaptureDeviceInput exposes a single video port; take the first.
                var videoPort = input.Ports.FirstOrDefault();
                if (videoPort == null)
                {
                    _logger?.LogError("Multi-cam: no video port on input for {Device}", device.LocalizedName);
                    _session.RemoveOutput(videoOutput);
                    _session.RemoveInput(input);
                    _session.CommitConfiguration();
                    videoOutput.Dispose();
                    input.Dispose();
                    return null;
                }

                var outputConnection = AVCaptureConnection.FromInputPorts(
                    new[] { videoPort }, videoOutput);
                if (!_session.CanAddConnection(outputConnection))
                {
                    _logger?.LogError("Multi-cam: cannot add video data connection for {Device}", device.LocalizedName);
                    _session.RemoveOutput(videoOutput);
                    _session.RemoveInput(input);
                    _session.CommitConfiguration();
                    outputConnection.Dispose();
                    videoOutput.Dispose();
                    input.Dispose();
                    return null;
                }
                _session.AddConnection(outputConnection);

                _session.CommitConfiguration();

                var registration = new MacMultiCamRegistration(
                    device, input, videoOutput, outputConnection, videoPort);
                _registrations.Add(registration);

                if (!_session.Running)
                {
                    _session.StartRunning();
                    _logger?.LogInformation("Multi-cam: session started ({Count} input(s))", _registrations.Count);
                }
                else
                {
                    _logger?.LogInformation("Multi-cam: input added live ({Count} input(s))", _registrations.Count);
                }

                return registration;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Multi-cam: registration threw for {Device}", device.LocalizedName);
                _session.CommitConfiguration();
                videoOutput.Dispose();
                input.Dispose();
                return null;
            }
        }
    }

    /// <summary>
    /// Removes <paramref name="reg"/> from the session. Stops the session entirely once
    /// the last registration is removed.
    /// </summary>
    public void Unregister(MacMultiCamRegistration reg)
    {
        if (reg == null) return;

        lock (_gate)
        {
            if (_session == null) return;
            if (!_registrations.Remove(reg)) return;

            _session.BeginConfiguration();
            try
            {
                if (reg.OutputConnection != null)
                {
                    _session.RemoveConnection(reg.OutputConnection);
                }
                _session.RemoveOutput(reg.VideoOutput);
                _session.RemoveInput(reg.Input);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Multi-cam: error during unregister cleanup");
            }
            finally
            {
                _session.CommitConfiguration();
            }

            reg.OutputConnection?.Dispose();
            reg.VideoOutput.Dispose();
            reg.Input.Dispose();

            if (_registrations.Count == 0 && _session.Running)
            {
                _session.StopRunning();
                _logger?.LogInformation("Multi-cam: session stopped (no inputs remaining)");
            }
        }
    }

    /// <summary>
    /// Adds a preview connection from the registration's video port to a new
    /// <see cref="AVCaptureVideoPreviewLayer"/>, attaches the layer to <paramref name="containerView"/>,
    /// and returns a handle to detach it later. Multi-cam sessions don't auto-connect, so this
    /// must build the connection explicitly. Only one slot in the UI ever shows a preview today,
    /// so this is called at most once concurrently — but the host doesn't enforce that.
    /// </summary>
    public MacMultiCamPreviewHandle? AttachPreview(MacMultiCamRegistration reg, UIView containerView)
    {
        if (reg == null) return null;

        lock (_gate)
        {
            if (_session == null)
            {
                _logger?.LogWarning("Multi-cam: AttachPreview called before session exists");
                return null;
            }

            AVCaptureVideoPreviewLayer? layer = null;
            AVCaptureConnection? connection = null;
            try
            {
                layer = AVCaptureVideoPreviewLayer.CreateWithNoConnection(_session);
                layer.VideoGravity = AVLayerVideoGravity.ResizeAspectFill;
                layer.Frame = containerView.Bounds;

                connection = new AVCaptureConnection(reg.VideoPort, layer);
                if (!_session.CanAddConnection(connection))
                {
                    _logger?.LogError("Multi-cam: cannot add preview connection for {Device}", reg.Device.LocalizedName);
                    connection.Dispose();
                    layer.Dispose();
                    return null;
                }

                _session.BeginConfiguration();
                _session.AddConnection(connection);
                _session.CommitConfiguration();

                containerView.Layer.AddSublayer(layer);

                _logger?.LogDebug("Multi-cam: preview attached for {Device}", reg.Device.LocalizedName);
                return new MacMultiCamPreviewHandle(layer, connection);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Multi-cam: AttachPreview threw for {Device}", reg.Device.LocalizedName);
                connection?.Dispose();
                layer?.Dispose();
                return null;
            }
        }
    }

    /// <summary>
    /// Removes the preview layer + connection created by <see cref="AttachPreview"/>.
    /// </summary>
    public void DetachPreview(MacMultiCamPreviewHandle handle)
    {
        if (handle == null) return;

        lock (_gate)
        {
            try
            {
                handle.Layer.RemoveFromSuperLayer();
                if (_session != null)
                {
                    _session.BeginConfiguration();
                    _session.RemoveConnection(handle.Connection);
                    _session.CommitConfiguration();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Multi-cam: DetachPreview cleanup threw");
            }

            handle.Connection.Dispose();
            handle.Layer.Dispose();
        }
    }

    private static AVCaptureDeviceFormat? SelectFormat(AVCaptureDevice device)
    {
        // Probe showed every format on every camera reports MultiCamSupported=true,
        // so we don't need to score by resolution — pick the first compatible one.
        // Fall back to ActiveFormat if filtering somehow yields nothing.
        return (device.Formats ?? Array.Empty<AVCaptureDeviceFormat>())
            .FirstOrDefault(f =>
            {
                try { return f.MultiCamSupported; }
                catch { return false; }
            }) ?? device.ActiveFormat;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_gate)
        {
            if (_session != null && _session.Running)
            {
                _session.StopRunning();
            }

            foreach (var reg in _registrations)
            {
                reg.OutputConnection?.Dispose();
                reg.VideoOutput.Dispose();
                reg.Input.Dispose();
            }
            _registrations.Clear();

            _session?.Dispose();
            _session = null;
        }
    }
}

/// <summary>
/// Handle returned by <see cref="MacMultiCamSessionHost.Register"/>. Carries the
/// per-camera resources owned by the host so the worker can attach its sample buffer
/// delegate to the video data output, and the host can build a preview connection
/// on demand from the same input video port.
/// </summary>
public sealed class MacMultiCamRegistration
{
    public AVCaptureDevice Device { get; }
    public AVCaptureDeviceInput Input { get; }
    public AVCaptureVideoDataOutput VideoOutput { get; }
    public AVCaptureConnection? OutputConnection { get; }
    public AVCaptureInputPort VideoPort { get; }

    internal MacMultiCamRegistration(
        AVCaptureDevice device,
        AVCaptureDeviceInput input,
        AVCaptureVideoDataOutput videoOutput,
        AVCaptureConnection? outputConnection,
        AVCaptureInputPort videoPort)
    {
        Device = device;
        Input = input;
        VideoOutput = videoOutput;
        OutputConnection = outputConnection;
        VideoPort = videoPort;
    }
}

/// <summary>
/// Handle returned by <see cref="MacMultiCamSessionHost.AttachPreview"/>. Pass back to
/// <see cref="MacMultiCamSessionHost.DetachPreview"/> to remove the preview connection
/// and dispose the layer.
/// </summary>
public sealed class MacMultiCamPreviewHandle
{
    public AVCaptureVideoPreviewLayer Layer { get; }
    public AVCaptureConnection Connection { get; }

    internal MacMultiCamPreviewHandle(AVCaptureVideoPreviewLayer layer, AVCaptureConnection connection)
    {
        Layer = layer;
        Connection = connection;
    }
}
