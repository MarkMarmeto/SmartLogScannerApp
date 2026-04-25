using Microsoft.Extensions.DependencyInjection;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.ViewModels;

namespace SmartLog.Scanner.Views;

/// <summary>
/// US0007/US0008/EP0011: Main scanning page.
/// Supports multi-camera QR scanning (1–8 cameras) and USB keyboard-wedge input.
/// </summary>
public partial class MainPage : ContentPage
{
    private MainViewModel? _viewModel;
    private string _scannerMode = "Camera";
    private bool _windowDestroyingHooked;

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

            // EP0011: Attach camera 0 preview after cameras are running
#if MACCATALYST
            if (_scannerMode == "Camera")
                AttachCameraPreview();
#elif WINDOWS
            if (_scannerMode == "Camera")
                AttachCameraPreview();
#endif

            // Focus the page for keyboard input in USB mode
            if (_scannerMode == "USB")
                this.Focus();
        }

        // EP0011: Hook Window.Destroying once to ensure StopAllAsync is called on app close
        if (!_windowDestroyingHooked && Window is not null)
        {
            Window.Destroying += OnWindowDestroying;
            _windowDestroyingHooked = true;
        }
    }

#if MACCATALYST
    /// <summary>
    /// EP0011: Attaches the AVCaptureVideoPreviewLayer from camera 0's headless worker
    /// to the CameraPreview0 view. Called after InitializeAsync so the capture session exists.
    /// </summary>
    private void AttachCameraPreview()
    {
        if (CameraPreview0?.Handler is Platforms.MacCatalyst.CameraPreviewHandler handler
            && _viewModel != null)
        {
            _viewModel.ConfigureCameraPreview(0, worker =>
            {
                if (worker is Platforms.MacCatalyst.CameraHeadlessWorker macWorker)
                    handler.AttachWorkerPreview(macWorker);
            });
        }
    }
#elif WINDOWS
    /// <summary>
    /// EP0011: Attaches camera 0's headless worker to the CameraPreview0 view's
    /// frame pump (~15 fps). Called after InitializeAsync so the capture session exists.
    /// </summary>
    private void AttachCameraPreview()
    {
        if (CameraPreview0?.Handler is Platforms.Windows.CameraPreviewHandler handler
            && _viewModel != null)
        {
            _viewModel.ConfigureCameraPreview(0, worker =>
            {
                if (worker is Platforms.Windows.CameraHeadlessWorker winWorker)
                    handler.AttachWorker(winWorker);
            });
        }
    }
#endif

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (_viewModel != null)
            await _viewModel.DisposeAsync();
    }

    /// <summary>
    /// EP0011: Ensures all camera threads are stopped when the app window closes.
    /// </summary>
    private async void OnWindowDestroying(object? sender, EventArgs e)
    {
        if (_viewModel != null)
            await _viewModel.StopCamerasAsync();
    }

    /// <summary>
    /// Navigate to setup/settings page to edit configuration.
    /// SetupPage is outside AppShell, so this goes through INavigationService
    /// which swaps Application.MainPage rather than using Shell navigation.
    /// </summary>
    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        var nav = Handler?.MauiContext?.Services.GetService<INavigationService>();
        if (nav != null)
            await nav.GoToAsync("//setup");
        else
            await Shell.Current.GoToAsync("//setup");
    }

    private async void OnAboutClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//about");
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
