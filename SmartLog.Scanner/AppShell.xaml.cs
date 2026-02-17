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

		// Navigation guard using file config
		Loaded += async (s, e) =>
		{
			var fileConfig = Handler?.MauiContext?.Services.GetService<FileConfigService>();
			if (fileConfig != null)
			{
				var config = await fileConfig.LoadConfigAsync();
				if (!config.SetupCompleted)
				{
					await GoToAsync("//setup");
				}
				else
				{
					await GoToAsync("//main");
				}
			}
			else
			{
				// Fallback to setup if service not available
				await GoToAsync("//setup");
			}
		};
	}
}
