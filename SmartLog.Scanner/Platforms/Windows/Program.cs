using Microsoft.UI.Xaml;

namespace SmartLog.Scanner.WinUI;

public class Program
{
	[STAThread]
	static void Main(string[] args)
	{
		WinRT.ComWrappersSupport.InitializeComWrappers();
		Application.Start((p) => {
			var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
				Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
			System.Threading.SynchronizationContext.SetSynchronizationContext(context);
			_ = new App();
		});
	}
}
