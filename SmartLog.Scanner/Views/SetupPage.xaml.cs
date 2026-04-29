using SmartLog.Scanner.Core.ViewModels;

namespace SmartLog.Scanner.Views;

/// <summary>
/// US0004: Device setup wizard page.
/// US0125: Compact two-column layout with sticky save bar; reflows to single column below 900 px wide.
/// </summary>
public partial class SetupPage : ContentPage
{
	private SetupViewModel? _viewModel;

	// US0125: 900 px breakpoint per Q4. Below this, the body's upper row collapses to single column.
	private const double TwoColumnBreakpointPx = 900;

	// Initial state matches the XAML default (two-column). OnSizeAllocated flips it if needed.
	private bool _isSingleColumn;

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

			// macOS Catalyst: native UIKit pickers don't honour SelectedItem set during the
			// initial render pass. Wait one frame then toggle each slot's SelectedDevice so
			// the picker re-reads the value and displays the correct selection.
			await Task.Delay(150);
			_viewModel.ForceRefreshSelections();
		}
	}

	/// <summary>
	/// US0125 AC9: Reflows the body Grid between two-column (≥900 px) and single-column (&lt;900 px)
	/// when the page width crosses the breakpoint. Guarded by _isSingleColumn so the reflow only fires
	/// on breakpoint crossings — not on every resize tick — to avoid layout-thrash flicker.
	/// </summary>
	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);

		if (width <= 0) return; // first measure before layout is real

		var shouldBeSingleColumn = width < TwoColumnBreakpointPx;
		if (shouldBeSingleColumn == _isSingleColumn) return;

		_isSingleColumn = shouldBeSingleColumn;
		ApplyResponsiveLayout(shouldBeSingleColumn);
	}

	/// <summary>
	/// Reorganises BodyGrid's row/column structure between the two layout modes.
	/// Two-column: 2 cols × 3 rows — Server (0,0), Security (0,1), Scanner (1, span 2), Camera (2, span 2).
	/// Single column: 1 col × 4 rows — Server (0,0), Security (1,0), Scanner (2,0), Camera (3,0).
	/// </summary>
	private void ApplyResponsiveLayout(bool singleColumn)
	{
		if (BodyGrid is null) return;

		BodyGrid.ColumnDefinitions.Clear();
		BodyGrid.RowDefinitions.Clear();

		if (singleColumn)
		{
			BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			for (int i = 0; i < 4; i++)
				BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

			Grid.SetRow(SecurityCard, 1);
			Grid.SetColumn(SecurityCard, 0);
			Grid.SetColumnSpan(SecurityCard, 1);

			Grid.SetRow(ScannerCard, 2);
			Grid.SetColumn(ScannerCard, 0);
			Grid.SetColumnSpan(ScannerCard, 1);

			Grid.SetRow(CameraCard, 3);
			Grid.SetColumn(CameraCard, 0);
			Grid.SetColumnSpan(CameraCard, 1);
		}
		else
		{
			BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			for (int i = 0; i < 3; i++)
				BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

			Grid.SetRow(SecurityCard, 0);
			Grid.SetColumn(SecurityCard, 1);
			Grid.SetColumnSpan(SecurityCard, 1);

			Grid.SetRow(ScannerCard, 1);
			Grid.SetColumn(ScannerCard, 0);
			Grid.SetColumnSpan(ScannerCard, 2);

			Grid.SetRow(CameraCard, 2);
			Grid.SetColumn(CameraCard, 0);
			Grid.SetColumnSpan(CameraCard, 2);
		}
	}
}
