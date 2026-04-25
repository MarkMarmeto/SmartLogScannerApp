using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Services;
using SmartLog.Scanner.Views;

namespace SmartLog.Scanner.Services;

/// <summary>
/// US0004: Implementation of INavigationService.
///
/// SetupPage lives outside AppShell because the Windows MAUI Shell platform
/// handler renders whichever ShellContent is at index 0 and silently ignores
/// later CurrentItem changes -- which made Setup &lt;-&gt; Main navigation
/// impossible. So GoToAsync("//setup") and GoToAsync("//main") swap
/// Application.Current.MainPage between SetupPage and a fresh AppShell.
/// All other routes (//logs, //queue, //about) stay inside AppShell and use
/// Shell.Current.GoToAsync as normal.
/// </summary>
public class ShellNavigationService : INavigationService
{
	private readonly ILogger<ShellNavigationService> _logger;
	private readonly IServiceProvider _services;

	public ShellNavigationService(
		ILogger<ShellNavigationService> logger,
		IServiceProvider services)
	{
		_logger = logger;
		_services = services;
	}

	public Task GoToAsync(string route)
	{
		// Setup is always outside AppShell -- swap MainPage.
		if (string.Equals(route, "//setup", StringComparison.OrdinalIgnoreCase))
			return SwapMainPageAsync(() => _services.GetRequiredService<SetupPage>(), "setup");

		// Going to //main: if we're currently on SetupPage, swap to a fresh
		// AppShell (which renders MainPage as its first/default ShellContent).
		// If we're already inside AppShell (e.g. coming back from Logs/Queue/
		// About), use Shell navigation -- those tab switches work fine on
		// Windows since AppShell is already initialized with Main at index 0.
		if (string.Equals(route, "//main", StringComparison.OrdinalIgnoreCase))
		{
			if (Application.Current?.MainPage is AppShell)
				return ShellGoToAsync(route);
			return SwapMainPageAsync(() => new AppShell(), "main");
		}

		return ShellGoToAsync(route);
	}

	private Task ShellGoToAsync(string route)
	{
		var shell = Shell.Current;
		if (shell is null)
		{
			_logger.LogWarning("GoToAsync({Route}) called but Shell.Current is null", route);
			return Task.CompletedTask;
		}

		var tcs = new TaskCompletionSource();
		shell.Dispatcher.Dispatch(async () =>
		{
			try
			{
				await shell.GoToAsync(route);
				tcs.TrySetResult();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Nav[{Route}] threw inside dispatcher", route);
				tcs.TrySetException(ex);
			}
		});
		return tcs.Task;
	}

	private Task SwapMainPageAsync(Func<Page> pageFactory, string label)
	{
		var app = Application.Current;
		if (app is null)
		{
			_logger.LogWarning("Nav[{Label}]: Application.Current is null", label);
			return Task.CompletedTask;
		}

		var tcs = new TaskCompletionSource();
		app.Dispatcher.Dispatch(() =>
		{
			try
			{
				_logger.LogInformation("Nav[{Label}]: swapping Application.MainPage", label);
				app.MainPage = pageFactory();
				tcs.TrySetResult();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Nav[{Label}]: failed to swap MainPage", label);
				tcs.TrySetException(ex);
			}
		});
		return tcs.Task;
	}
}
