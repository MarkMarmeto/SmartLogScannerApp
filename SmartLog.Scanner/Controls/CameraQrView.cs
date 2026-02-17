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

    public bool IsDetecting
    {
        get => (bool)GetValue(IsDetectingProperty);
        set => SetValue(IsDetectingProperty, value);
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
