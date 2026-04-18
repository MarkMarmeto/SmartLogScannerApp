namespace SmartLog.Scanner.Controls;

/// <summary>
/// Cross-platform camera QR scanner view.
/// Uses native camera implementations on each platform.
/// </summary>
public class CameraQrView : View
{
    /// <summary>
    /// EP0011: Zero-based index of this camera in the multi-camera grid.
    /// Used by the shared BarcodeDetected handler in MainPage to route events to the correct camera.
    /// </summary>
    public static readonly BindableProperty CameraIndexProperty =
        BindableProperty.Create(nameof(CameraIndex), typeof(int), typeof(CameraQrView), 0);

    public int CameraIndex
    {
        get => (int)GetValue(CameraIndexProperty);
        set => SetValue(CameraIndexProperty, value);
    }

    /// <summary>
    /// EP0011: Frame skip count for adaptive decode throttle.
    /// Platform handler reads this value and only forwards a barcode event every N-th frame.
    /// </summary>
    public static readonly BindableProperty ThrottleFramesProperty =
        BindableProperty.Create(nameof(ThrottleFrames), typeof(int), typeof(CameraQrView), 5);

    public int ThrottleFrames
    {
        get => (int)GetValue(ThrottleFramesProperty);
        set => SetValue(ThrottleFramesProperty, value);
    }

    public static readonly BindableProperty IsDetectingProperty =
        BindableProperty.Create(nameof(IsDetecting), typeof(bool), typeof(CameraQrView), false,
            propertyChanged: OnIsDetectingChanged);

    public static readonly BindableProperty SelectedCameraIdProperty =
        BindableProperty.Create(nameof(SelectedCameraId), typeof(string), typeof(CameraQrView), string.Empty);

    public bool IsDetecting
    {
        get => (bool)GetValue(IsDetectingProperty);
        set => SetValue(IsDetectingProperty, value);
    }

    /// <summary>
    /// Platform-specific device ID of the camera to use.
    /// Empty string means "system default".
    /// </summary>
    public string SelectedCameraId
    {
        get => (string)GetValue(SelectedCameraIdProperty);
        set => SetValue(SelectedCameraIdProperty, value);
    }

    public event EventHandler<string>? BarcodeDetected;

    internal void RaiseBarcodeDetected(string value)
    {
        BarcodeDetected?.Invoke(this, value);
    }

    private static void OnIsDetectingChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CameraQrView view)
        {
            view.OnIsDetectingChanged((bool)newValue);
        }
    }

    protected virtual void OnIsDetectingChanged(bool isDetecting)
    {
        // Handled by platform-specific handler
    }
}
