using Avalonia.Controls;
using SharpEyes.ViewModels;

namespace SharpEyes.Views
{
	public partial class SettingsWindow : Window
	{
		public SettingsWindow()
		{
			DataContext = new SettingsWindowViewModel();
			InitializeComponent();
		}
	}
}
