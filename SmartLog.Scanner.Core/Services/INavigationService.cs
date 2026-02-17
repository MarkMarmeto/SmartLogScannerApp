namespace SmartLog.Scanner.Core.Services;

/// <summary>
/// US0004: Abstraction for page navigation.
/// Enables testable ViewModels by decoupling from Shell.Current static API.
/// </summary>
public interface INavigationService
{
	/// <summary>
	/// Navigate to the specified route.
	/// </summary>
	/// <param name="route">The route path (e.g., "//main", "//setup")</param>
	Task GoToAsync(string route);
}
