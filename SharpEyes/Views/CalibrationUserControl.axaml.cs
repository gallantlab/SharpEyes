using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using SharpEyes.ViewModels;

namespace SharpEyes.Views
{
	public partial class CalibrationUserControl : UserControl
	{
		private CalibrationViewModel? viewModel => DataContext as CalibrationViewModel;

		public CalibrationUserControl()
		{
			InitializeComponent();
			foreach (StackPanel panel in new StackPanel[] { DataSettingsPanel, CalibrationParametersPanel, PreprocessingPanel })
			{
				panel.AddHandler(LostFocusEvent, OnSettingAccepted);
				panel.AddHandler(KeyDownEvent, OnSettingKeyDown);
				panel.AddHandler(ToggleButton.CheckedEvent, OnSettingAccepted);
				panel.AddHandler(ToggleButton.UncheckedEvent, OnSettingAccepted);
			}
		}

		/// <summary>
		/// Commits the calibration settings when a control in a settings panel loses focus or a toggle is switched.
		/// </summary>
		/// <param name="sender">The panel that received the routed event.</param>
		/// <param name="e">Event data.</param>
		private void OnSettingAccepted(object? sender, RoutedEventArgs e)
		{
			viewModel?.CommitCalibrationSettings();
		}

		/// <summary>
		/// Commits the calibration settings when Enter is pressed in a settings panel.
		/// </summary>
		/// <param name="sender">The panel that received the routed event.</param>
		/// <param name="e">Event data.</param>
		private void OnSettingKeyDown(object? sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
				viewModel?.CommitCalibrationSettings();
		}
	}
}
