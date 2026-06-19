using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using System;
using System.IO;
using SharpEyes.Views;

namespace SharpEyes
{
	class Program
	{
		// Initialization code. Don't use any Avalonia, third-party APIs or any
		// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
		// yet and stuff might break.
		[STAThread]
		public static void Main(string[] args)
		{
			AppBuilder builder = BuildAvaloniaApp();
#if !DEBUG
			try
			{
				builder.StartWithClassicDesktopLifetime(args);
			}
			catch (Exception e)
			{
				if (builder.Instance.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
					window.GlobalExceptionHandler(e);
			}
#else
			Console.SetOut(TextWriter.Null);
			builder.StartWithClassicDesktopLifetime(args);
#endif
		}

		// Avalonia configuration, don't remove; also used by visual designer.
		public static AppBuilder BuildAvaloniaApp()
			=> AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.LogToTrace()
				.UseReactiveUI();
	}
}
