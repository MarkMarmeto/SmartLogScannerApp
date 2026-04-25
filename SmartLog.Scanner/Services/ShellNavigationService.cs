using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Services;

/// <summary>
/// US0004: MAUI Shell implementation of INavigationService.
/// Wraps Shell.Current.GoToAsync for dependency injection.
/// </summary>
public class ShellNavigationService : INavigationService
{
	public Task GoToAsync(string route)
	{
		var shell = Shell.Current;
		if (shell is null)
			return Task.CompletedTask;

		// Windows MAUI Shell occasionally no-ops a "//route" navigation when called
		// inline from a RelayCommand's awaited handler (e.g. Setup -> Main after
		// SaveCommand). Dispatching onto the next UI cycle gives Shell a chance to
		// release the prior handler state before switching the active ShellContent.
		var tcs = new TaskCompletionSource();
		shell.Dispatcher.Dispatch(async () =>
		{
			try
			{
				await shell.GoToAsync(route);

				// Defensive fallback for the same Windows Shell bug: if the route
				// targets a TabBar ShellContent and the active ShellItem didn't
				// change, switch CurrentItem explicitly.
				if (route.StartsWith("//"))
				{
					var targetRoute = route.Substring(2);
					var match = FindShellItem(shell, targetRoute);
					if (match is not null && !ReferenceEquals(shell.CurrentItem, match))
					{
						shell.CurrentItem = match;
					}
				}

				tcs.TrySetResult();
			}
			catch (Exception ex)
			{
				tcs.TrySetException(ex);
			}
		});
		return tcs.Task;
	}

	private static ShellItem? FindShellItem(Shell shell, string route)
	{
		foreach (var item in shell.Items)
		{
			foreach (var section in item.Items)
			{
				foreach (var content in section.Items)
				{
					if (string.Equals(content.Route, route, StringComparison.OrdinalIgnoreCase))
						return item;
				}
			}
		}
		return null;
	}
}
