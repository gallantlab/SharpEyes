using System;
using Avalonia.Controls;
using ReactiveUI;
using SharpEyes.ViewModels;

namespace SharpEyes.Views
{
	public partial class SettingsWindow : Window
	{
		private readonly PythonSettingsUserControl _pythonSettingsControl = new PythonSettingsUserControl();
		private readonly VideoPlaybackSettingsUserControl _videoPlaybackSettingsControl;

		public SettingsWindow() : this(() => { }) { }

		public SettingsWindow(Action refreshActiveTab)
		{
			_videoPlaybackSettingsControl = new VideoPlaybackSettingsUserControl();
			_videoPlaybackSettingsControl.DataContext = new VideoPlaybackSettingsViewModel(refreshActiveTab);

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
				0 => _pythonSettingsControl,
				1 => _videoPlaybackSettingsControl,
				_ => null
			};
		}
	}
}
