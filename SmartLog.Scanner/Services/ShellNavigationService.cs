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

				// Defensive fallback for the same Windows Shell bug. AppShell.xaml
				// uses a single <TabBar> with multiple <ShellContent> children, so
				// MAUI wraps each ShellContent in an implicit Tab (ShellSection)
				// under one ShellItem. shell.CurrentItem is always that TabBar --
				// switching tabs means setting CurrentItem.CurrentItem (the
				// active ShellSection), not CurrentItem itself.
				if (route.StartsWith("//"))
				{
					var targetRoute = route.Substring(2);
					if (TryFindRoute(shell, targetRoute, out var item, out var section, out var content))
					{
						if (!ReferenceEquals(shell.CurrentItem, item))
							shell.CurrentItem = item;
						if (!ReferenceEquals(item!.CurrentItem, section))
							item.CurrentItem = section;
						if (!ReferenceEquals(section!.CurrentItem, content))
							section.CurrentItem = content;
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

	private static bool TryFindRoute(
		Shell shell,
		string route,
		out ShellItem? item,
		out ShellSection? section,
		out ShellContent? content)
	{
		foreach (var i in shell.Items)
		{
			foreach (var s in i.Items)
			{
				foreach (var c in s.Items)
				{
					if (string.Equals(c.Route, route, StringComparison.OrdinalIgnoreCase))
					{
						item = i;
						section = s;
						content = c;
						return true;
					}
				}
			}
		}
		item = null;
		section = null;
		content = null;
		return false;
	}
}
