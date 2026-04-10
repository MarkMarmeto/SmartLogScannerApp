namespace SmartLog.Scanner.Controls;

/// <summary>
/// Cross-platform camera QR scanner view.
/// Uses native camera implementations on each platform.
/// </summary>
public class CameraQrView : View
{
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
