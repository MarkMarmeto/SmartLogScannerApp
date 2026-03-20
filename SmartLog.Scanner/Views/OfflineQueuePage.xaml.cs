using SmartLog.Scanner.ViewModels;

namespace SmartLog.Scanner.Views;

public partial class OfflineQueuePage : ContentPage
{
    private readonly OfflineQueueViewModel _viewModel;

    public OfflineQueuePage(OfflineQueueViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//main");
    }
}
