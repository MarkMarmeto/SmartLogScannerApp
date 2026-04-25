using Microsoft.Extensions.Logging;
using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Services;

/// <summary>
/// US0004: MAUI Shell implementation of INavigationService.
/// Wraps Shell.Current.GoToAsync for dependency injection.
/// </summary>
public class ShellNavigationService : INavigationService
{
	private readonly ILogger<ShellNavigationService> _logger;

	public ShellNavigationService(ILogger<ShellNavigationService> logger)
	{
		_logger = logger;
	}

	public Task GoToAsync(string route)
	{
		var shell = Shell.Current;
		if (shell is null)
		{
			_logger.LogWarning("GoToAsync({Route}) called but Shell.Current is null", route);
			return Task.CompletedTask;
		}

		// Windows MAUI Shell occasionally no-ops a "//route" navigation when called
		// inline from a RelayCommand's awaited handler (e.g. Setup -> Main after
		// SaveCommand). Dispatching onto the next UI cycle gives Shell a chance to
		// release the prior handler state before switching the active ShellContent.
		var tcs = new TaskCompletionSource();
		shell.Dispatcher.Dispatch(async () =>
		{
			try
			{
				_logger.LogInformation(
					"Nav[{Route}] before GoToAsync: shell.CurrentItem={Item}, item.CurrentItem={Section}, section.CurrentItem={Content}, navStack={NavStack}, modalStack={ModalStack}",
					route,
					DescribeItem(shell.CurrentItem),
					DescribeItem(shell.CurrentItem?.CurrentItem),
					DescribeItem(shell.CurrentItem?.CurrentItem?.CurrentItem),
					DescribeStack(shell.Navigation.NavigationStack),
					DescribeStack(shell.Navigation.ModalStack));

				// Windows MAUI sometimes pushes a "//route" target onto the navigation
				// stack instead of switching ShellSections (so the model thinks we're
				// still on the previous tab and the new page is a stack entry on top).
				// Pop the stack down to the section root before navigating, otherwise
				// our section-toggle below has nothing to redraw against.
				while (shell.Navigation.NavigationStack.Count > 1)
				{
					_logger.LogInformation("Nav[{Route}] popping NavigationStack entry: {Page}",
						route, shell.Navigation.NavigationStack[^1]?.GetType().Name ?? "null");
					await shell.Navigation.PopAsync(false);
				}
				while (shell.Navigation.ModalStack.Count > 0)
				{
					_logger.LogInformation("Nav[{Route}] popping ModalStack entry: {Page}",
						route, shell.Navigation.ModalStack[^1]?.GetType().Name ?? "null");
					await shell.Navigation.PopModalAsync(false);
				}

				await shell.GoToAsync(route);

				_logger.LogInformation(
					"Nav[{Route}] after GoToAsync: shell.CurrentItem={Item}, item.CurrentItem={Section}, section.CurrentItem={Content}",
					route,
					DescribeItem(shell.CurrentItem),
					DescribeItem(shell.CurrentItem?.CurrentItem),
					DescribeItem(shell.CurrentItem?.CurrentItem?.CurrentItem));

				// Defensive fallback for the same Windows Shell bug. AppShell.xaml
				// uses a single <TabBar> with multiple <ShellContent> children, so
				// MAUI wraps each ShellContent in an implicit Tab (ShellSection)
				// under one ShellItem. shell.CurrentItem is always that TabBar --
				// switching tabs means setting CurrentItem.CurrentItem (the
				// active ShellSection), not CurrentItem itself.
				if (route.StartsWith("//"))
				{
					var targetRoute = route.Substring(2);
					var dump = DumpShellHierarchy(shell);
					if (TryFindRoute(shell, targetRoute, out var item, out var section, out var content))
					{
						_logger.LogInformation(
							"Nav[{Route}] resolved hierarchy: item={Item}, section={Section}, content={Content}. Tree: {Tree}",
							route, DescribeItem(item), DescribeItem(section), DescribeItem(content), dump);

						if (!ReferenceEquals(shell.CurrentItem, item))
						{
							_logger.LogInformation("Nav[{Route}] setting shell.CurrentItem -> {Item}", route, DescribeItem(item));
							shell.CurrentItem = item;
						}

						// Windows MAUI Shell desync: a prior raw Shell.GoToAsync (e.g. from
						// MainPage's Settings button) can render a different ShellContent
						// without updating item.CurrentItem. When that happens, the model
						// claims we're already at the target, so re-assigning the same
						// reference is a no-op and the visual never updates. Force a real
						// PropertyChanged by toggling through a different section first.
						if (ReferenceEquals(item!.CurrentItem, section))
						{
							ShellSection? bypass = null;
							foreach (var s in item.Items)
							{
								if (!ReferenceEquals(s, section))
								{
									bypass = s;
									break;
								}
							}
							if (bypass != null)
							{
								_logger.LogInformation(
									"Nav[{Route}] item.CurrentItem already == target; toggling through {Bypass} to force change",
									route, DescribeItem(bypass));
								item.CurrentItem = bypass;
							}
						}
						_logger.LogInformation("Nav[{Route}] setting item.CurrentItem -> {Section}", route, DescribeItem(section));
						item.CurrentItem = section;

						if (!ReferenceEquals(section!.CurrentItem, content))
						{
							_logger.LogInformation("Nav[{Route}] setting section.CurrentItem -> {Content}", route, DescribeItem(content));
							section.CurrentItem = content;
						}

						_logger.LogInformation(
							"Nav[{Route}] after fallback: shell.CurrentItem={Item}, item.CurrentItem={Section}, section.CurrentItem={Content}",
							route,
							DescribeItem(shell.CurrentItem),
							DescribeItem(shell.CurrentItem?.CurrentItem),
							DescribeItem(shell.CurrentItem?.CurrentItem?.CurrentItem));
					}
					else
					{
						_logger.LogWarning(
							"Nav[{Route}] TryFindRoute did NOT find a matching ShellContent. Tree: {Tree}",
							route, dump);
					}
				}

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

	private static string DescribeItem(BaseShellItem? item)
	{
		if (item is null) return "null";
		var route = Routing.GetRoute(item) ?? "(no-route)";
		return $"{item.GetType().Name}#{item.GetHashCode():x}({route})";
	}

	private static string DescribeStack(IReadOnlyList<Page> stack)
	{
		if (stack == null || stack.Count == 0) return "[]";
		var parts = new List<string>(stack.Count);
		for (int i = 0; i < stack.Count; i++)
		{
			var p = stack[i];
			parts.Add(p == null ? "null" : $"{p.GetType().Name}#{p.GetHashCode():x}");
		}
		return "[" + string.Join(", ", parts) + "]";
	}

	private static string DumpShellHierarchy(Shell shell)
	{
		var lines = new List<string>();
		foreach (var i in shell.Items)
		{
			lines.Add($"item={DescribeItem(i)}");
			foreach (var s in i.Items)
			{
				lines.Add($"  section={DescribeItem(s)}");
				foreach (var c in s.Items)
					lines.Add($"    content={DescribeItem(c)}");
			}
		}
		return string.Join(" | ", lines);
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
					var contentRoute = Routing.GetRoute(c) ?? c.Route;
					if (string.Equals(contentRoute, route, StringComparison.OrdinalIgnoreCase) ||
					    string.Equals(c.Title, route, StringComparison.OrdinalIgnoreCase))
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
