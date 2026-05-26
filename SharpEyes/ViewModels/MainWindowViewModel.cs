using System;
using System.Collections.Generic;
using System.Text;
using ReactiveUI;

namespace SharpEyes.ViewModels
{
	public class MainWindowViewModel : ViewModelBase
	{
		public PupilFindingUserControlViewModel pupilFindingUserControlViewModel { get; }
		public StimulusGazeViewModel stimulusGazeViewModel { get; }
		public CalibrationViewModel calibrationViewModel { get; }
		public RecenteringViewModel recenteringViewModel { get; }

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
			stimulusGazeViewModel.RecenteringViewModel = recenteringViewModel;
			stimulusGazeViewModel.SwitchToRecenteringTab = () => SelectedTabIndex = 3;
		}
	}
}
