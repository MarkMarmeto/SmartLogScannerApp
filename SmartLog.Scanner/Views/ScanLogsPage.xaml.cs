using SmartLog.Scanner.Core.ViewModels;

namespace SmartLog.Scanner.Views;

public partial class ScanLogsPage : ContentPage
{
    private readonly ScanLogsViewModel _viewModel;

    public ScanLogsPage(ScanLogsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Load logs when page appears
        await _viewModel.InitializeAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//main");
    }
}
