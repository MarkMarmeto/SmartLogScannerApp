using SmartLog.Scanner.ViewModels;

namespace SmartLog.Scanner.Views;

/// <summary>
/// US0007/US0008: Main scanning page with camera QR code detection and USB keyboard wedge input.
/// Modern UI with visual feedback for scan results.
/// </summary>
public partial class MainPage : ContentPage
{
    private MainViewModel? _viewModel;
    private string _scannerMode = "Camera";

    // Parameterless constructor for DataTemplate
    public MainPage()
    {
        InitializeComponent();
    }

    // DI constructor
    public MainPage(MainViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        BindingContext = _viewModel;

        // Read scanner mode from preferences
        _scannerMode = Preferences.Get("Scanner.Mode", "Camera");

        // Enable keyboard input for USB scanner mode
        if (_scannerMode == "USB")
        {
            this.Focused += OnPageFocused;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel != null)
        {
            await _viewModel.InitializeAsync();

            // Focus the page for keyboard input in USB mode
            if (_scannerMode == "USB")
            {
                this.Focus();
            }
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (_viewModel != null)
        {
            await _viewModel.DisposeAsync();
        }
    }

    /// <summary>
    /// Navigate to setup/settings page to edit configuration.
    /// </summary>
    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//setup");
    }

    /// <summary>
    /// Navigate to scan logs viewer page.
    /// </summary>
    private async void OnViewLogsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//logs");
    }

    /// <summary>
    /// Navigate to offline queue management page.
    /// </summary>
    private async void OnViewQueueClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//queue");
    }

    /// <summary>
    /// US0007: Handle QR code detection from camera.
    /// </summary>
    private void OnBarcodeDetected(object? sender, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            System.Diagnostics.Debug.WriteLine("[MainPage] OnBarcodeDetected called with null/empty value");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[MainPage] OnBarcodeDetected called with value: {value.Substring(0, Math.Min(50, value.Length))}...");

        // Only process in Camera mode
        if (_scannerMode != "Camera")
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] Not in Camera mode (current: {_scannerMode}), ignoring barcode");
            return;
        }

        System.Diagnostics.Debug.WriteLine("[MainPage] Processing QR code via ViewModel...");

        // Process the QR code payload via ViewModel
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_viewModel != null)
            {
                System.Diagnostics.Debug.WriteLine("[MainPage] Calling ProcessCameraQrCodeAsync...");
                await _viewModel.ProcessCameraQrCodeAsync(value);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[MainPage] ERROR: ViewModel is null!");
            }
        });
    }

    /// <summary>
    /// US0008: Handle page focus for USB scanner keyboard input.
    /// </summary>
    private void OnPageFocused(object? sender, FocusEventArgs e)
    {
        // Ensure page captures keyboard events
        this.Focus();
    }

    /// <summary>
    /// US0008: Override keyboard input to capture USB scanner keystrokes.
    /// This is platform-specific and will be implemented in platform handlers.
    /// </summary>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // Resolve ViewModel from DI if not already set (for DataTemplate scenarios)
        if (_viewModel == null && Handler?.MauiContext != null)
        {
            _viewModel = Handler.MauiContext.Services.GetService<MainViewModel>();
            if (_viewModel != null)
            {
                BindingContext = _viewModel;

                // Read scanner mode from preferences
                _scannerMode = Preferences.Get("Scanner.Mode", "Camera");

                // Enable keyboard input for USB scanner mode
                if (_scannerMode == "USB")
                {
                    this.Focused += OnPageFocused;
                }
            }
        }

#if MACCATALYST
        if (_scannerMode == "USB" && Handler?.PlatformView is UIKit.UIView view)
        {
            // MacCatalyst: Attach NSEvent monitor for keyboard input
            AttachMacKeyboardHandler(view);
        }
#endif
    }

#if MACCATALYST
    private void AttachMacKeyboardHandler(UIKit.UIView view)
    {
        // Make the view first responder to receive keyboard events
        view.BecomeFirstResponder();

        // Note: Full keyboard event monitoring requires platform-specific NSEvent handling
        // For now, we'll use a text field approach as a workaround
        var textField = new UIKit.UITextField
        {
            Hidden = true,
            Alpha = 0,
            Frame = new CoreGraphics.CGRect(0, 0, 1, 1)
        };

        textField.EditingChanged += (sender, e) =>
        {
            if (textField.Text?.Length > 0 && _viewModel != null)
            {
                var character = textField.Text[^1].ToString();
                _viewModel.ProcessKeystroke(character);
            }
        };

        textField.EditingDidEnd += (sender, e) =>
        {
            // Enter key pressed
            if (_viewModel != null)
            {
                _viewModel.ProcessEnterKey();
            }
            textField.Text = "";
            textField.BecomeFirstResponder(); // Keep focus
        };

        view.AddSubview(textField);
        textField.BecomeFirstResponder();
    }
#endif
}
