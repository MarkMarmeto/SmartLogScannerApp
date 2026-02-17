using SmartLog.Scanner.Core.Services;

namespace SmartLog.Scanner.Services;

/// <summary>
/// US0004: MAUI Shell implementation of INavigationService.
/// Wraps Shell.Current.GoToAsync for dependency injection.
/// </summary>
public class ShellNavigationService : INavigationService
{
	public async Task GoToAsync(string route)
	{
		await Shell.Current.GoToAsync(route);
	}
}
