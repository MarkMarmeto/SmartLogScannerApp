namespace SmartLog.Scanner;

/// <summary>
/// Shell containing post-setup pages (Main, Logs, Queue, About).
/// SetupPage lives outside the Shell and is shown by swapping
/// Application.MainPage in INavigationService -- see App.xaml.cs and
/// ShellNavigationService.GoToAsync. With Setup removed from the Shell,
/// MainPage is the first (default) ShellContent so the Windows Shell
/// platform handler renders it correctly without further navigation.
/// </summary>
public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
	}
}
