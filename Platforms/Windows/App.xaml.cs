using Microsoft.UI.Xaml;
using MD.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MD.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		// WinUI 的 UI 线程异常默认会让进程直接消失，至少先留一份带堆栈的日志。
		// 不设 e.Handled = true：吞掉异常会让界面停在半损坏状态，更难排查。
		UnhandledException += (_, e) => DiagnosticsLog.Write("WinUI UnhandledException", e.Exception);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

