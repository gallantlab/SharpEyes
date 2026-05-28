using System.Reactive;
using System.Reactive.Linq;
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

		private int _selectedTabIndex;
		public int SelectedTabIndex
		{
			get => _selectedTabIndex;
			set
			{
				this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
				SharpEyes.Models.Settings.Current.LastOpenTabIndex = value;
				SharpEyes.Models.Settings.Current.Save();
				if (value == 4) motionEnergyViewModel.InitializePython();
				UpdateCurrentTabTimecodeDisplays();
			}
		}

		public void UpdateCurrentTabTimecodeDisplays()
		{
			switch (_selectedTabIndex)
			{
				case 0: pupilFindingUserControlViewModel.UpdateTimecodeDisplay(); break;
				case 2: stimulusGazeViewModel.UpdateTimecodeDisplay(); break;
				case 3: recenteringViewModel.UpdateTimecodeDisplay(); break;
				case 4: motionEnergyViewModel.UpdateTimecodeDisplay(); break;
			}
		}

		public MainWindowViewModel()
		{
			SharpEyes.Models.PythonEnvironmentManager.Instance.LoadSettings();
			_selectedTabIndex = SharpEyes.Models.Settings.Current.LastOpenTabIndex;
			pupilFindingUserControlViewModel = new PupilFindingUserControlViewModel();
			stimulusGazeViewModel = new StimulusGazeViewModel();
			calibrationViewModel = new CalibrationViewModel();
			recenteringViewModel = new RecenteringViewModel();
			motionEnergyViewModel = new MotionEnergyViewModel();
			stimulusGazeViewModel.RecenteringViewModel = recenteringViewModel;
			stimulusGazeViewModel.SwitchToRecenteringTab = () => SelectedTabIndex = 3;
			recenteringViewModel.MotionEnergyViewModel = motionEnergyViewModel;
			recenteringViewModel.SwitchToMotionEnergyTab = () => SelectedTabIndex = 4;

			OpenSettingsCommand = ReactiveCommand.Create(() =>
			{
				SettingsWindow settingsWindow = new SettingsWindow(UpdateCurrentTabTimecodeDisplays);
				settingsWindow.ShowDialog(MainWindow);
			});

		}
	}
}
