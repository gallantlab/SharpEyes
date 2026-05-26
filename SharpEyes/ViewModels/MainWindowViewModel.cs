using System;
using System.Collections.Generic;
using System.Text;

namespace SharpEyes.ViewModels
{
	public class MainWindowViewModel : ViewModelBase
	{
		public PupilFindingUserControlViewModel pupilFindingUserControlViewModel { get; }
		public StimulusGazeViewModel stimulusGazeViewModel { get; }
		public CalibrationViewModel calibrationViewModel { get; }
		public RecenteringViewModel recenteringViewModel { get; }

		public MainWindowViewModel()
		{
			pupilFindingUserControlViewModel = new PupilFindingUserControlViewModel();
			stimulusGazeViewModel = new StimulusGazeViewModel();
			calibrationViewModel = new CalibrationViewModel();
			recenteringViewModel = new RecenteringViewModel();
		}
	}
}
