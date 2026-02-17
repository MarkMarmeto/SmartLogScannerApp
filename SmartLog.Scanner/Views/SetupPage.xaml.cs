using SmartLog.Scanner.Core.ViewModels;

namespace SmartLog.Scanner.Views;

/// <summary>
/// US0004: Device setup wizard page.
/// Configures server URL, API key, HMAC secret, scan mode, and scan type on first launch.
/// </summary>
public partial class SetupPage : ContentPage
{
	private SetupViewModel? _viewModel;

	// Parameterless constructor for DataTemplate
	public SetupPage()
	{
		InitializeComponent();
	}

	// DI constructor
	public SetupPage(SetupViewModel viewModel) : this()
	{
		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();

		// Resolve ViewModel from DI if not already set
		if (_viewModel == null && Handler?.MauiContext != null)
		{
			_viewModel = Handler.MauiContext.Services.GetService<SetupViewModel>();
			if (_viewModel != null)
			{
				BindingContext = _viewModel;
			}
		}
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_viewModel != null)
		{
			await _viewModel.InitializeAsync();
		}
	}
}
