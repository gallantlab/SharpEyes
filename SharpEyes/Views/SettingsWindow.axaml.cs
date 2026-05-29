using System;
using Avalonia.Controls;
using ReactiveUI;
using SharpEyes.ViewModels;

namespace SharpEyes.Views
{
	public partial class SettingsWindow : Window
	{
		private readonly PythonSettingsUserControl _pythonSettingsControl = new PythonSettingsUserControl();
		private readonly GeneralSettingsUserControl generalSettingsControl;

		public SettingsWindow() : this(() => { }) { }

		public SettingsWindow(Action refreshActiveTab)
		{
			generalSettingsControl = new GeneralSettingsUserControl();
			generalSettingsControl.DataContext = new GeneralSettingsViewModel(refreshActiveTab);

			SettingsWindowViewModel viewModel = new SettingsWindowViewModel();
			DataContext = viewModel;
			InitializeComponent();

			viewModel.WhenAnyValue(x => x.SelectedCategoryIndex)
				.Subscribe(UpdateContentArea);
			UpdateContentArea(viewModel.SelectedCategoryIndex);
		}

		private void UpdateContentArea(int selectedIndex)
		{
			ContentArea.Child = selectedIndex switch
			{
				0 => generalSettingsControl,
				1 => _pythonSettingsControl,
				_ => null
			};
		}
	}
}
