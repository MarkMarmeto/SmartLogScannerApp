using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner;

/// <summary>
/// US0004: App shell with navigation guard for setup flow.
/// Routes to SetupPage on first launch, MainPage on subsequent launches.
/// </summary>
public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Navigation guard using preferences
		Loaded += async (s, e) =>
		{
			var preferences = Handler?.MauiContext?.Services.GetService<IPreferencesService>();
			if (preferences != null && preferences.GetSetupCompleted())
			{
				await GoToAsync("//main");
			}
			else
			{
				await GoToAsync("//setup");
			}
		};
	}
}
