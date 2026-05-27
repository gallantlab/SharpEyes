using Avalonia.Controls;
using SharpEyes.ViewModels;

namespace SharpEyes.Views
{
	public partial class PythonSettingsUserControl : UserControl
	{
		public PythonSettingsUserControl()
		{
			DataContext = new PythonSettingsViewModel();
			InitializeComponent();
		}
	}
}
