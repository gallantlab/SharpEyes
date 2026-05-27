using System.Reactive;
using ReactiveUI;
using SharpEyes.Views;

namespace SharpEyes.ViewModels
{
	public class MainWindowViewModel : ViewModelBase
	{
		public PupilFindingUserControlViewModel pupilFindingUserControlViewModel { get; }
		public StimulusGazeViewModel stimulusGazeViewModel { get; }
		public CalibrationViewModel calibrationViewModel { get; }
		public RecenteringViewModel recenteringViewModel { get; }
		public MotionEnergyViewModel motionEnergyViewModel { get; }

		public ReactiveCommand<Unit, Unit>? OpenSettingsCommand { get; } = null;

		private int _selectedTabIndex = 2;
		public int SelectedTabIndex
		{
			get => _selectedTabIndex;
			set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
		}

		public MainWindowViewModel()
		{
			pupilFindingUserControlViewModel = new PupilFindingUserControlViewModel();
			stimulusGazeViewModel = new StimulusGazeViewModel();
			calibrationViewModel = new CalibrationViewModel();
			recenteringViewModel = new RecenteringViewModel();
			motionEnergyViewModel = new MotionEnergyViewModel();
			stimulusGazeViewModel.RecenteringViewModel = recenteringViewModel;
			stimulusGazeViewModel.SwitchToRecenteringTab = () => SelectedTabIndex = 3;

			OpenSettingsCommand = ReactiveCommand.Create(() =>
			{
				SettingsWindow settingsWindow = new SettingsWindow();
				settingsWindow.ShowDialog(MainWindow);
			});

			SharpEyes.Models.PythonEnvironmentManager.Instance.LoadSettings();
		}
	}
}
