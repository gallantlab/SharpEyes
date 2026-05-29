using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ReactiveUI;
using Eyetracking;
using SharpEyes.Views;
using Avalonia.Controls;
using Num = NumSharp.np;

namespace SharpEyes.ViewModels
{
	public class CalibrationViewModel : ViewModelBase
	{
		public ReactiveCommand<Unit, Unit> LoadCalibrationPupilsCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ImportCalibrationPupilsCommand { get; set; }

		public ReactiveCommand<Unit, Unit> LoadPupilsToConvertCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ConvertPupilFinderCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ComputeMappingCommand { get; set; }

		public ReactiveCommand<Unit, Unit> ForceRedrawCommand { get; set; }

		private int _stimulusWidth = 1024;
		public int StimulusWidth
		{
			get => _stimulusWidth;
			set => this.RaiseAndSetIfChanged(ref _stimulusWidth, value);
		}

		private int _stimulusHeight = 768;
		public int StimulusHeight
		{
			get => _stimulusHeight;
			set => this.RaiseAndSetIfChanged(ref _stimulusHeight, value);
		}

		private string _calibrationRMSError = "Mapping not yet computed";
		public string CalibrationRMSError
		{
			get => _calibrationRMSError;
			set => this.RaiseAndSetIfChanged(ref _calibrationRMSError, value);
		}

		private int _calibrationStartFrame = 1;
		public int CalibrationStartFrame
		{
			get => _calibrationStartFrame;
			set => this.RaiseAndSetIfChanged(ref _calibrationStartFrame, value);
		}

		private string _calibrationStartTimeStamp = "0:00:00.000";	
		public string CalibrationStartTimeStamp
		{
			get => _calibrationStartTimeStamp;
			set => this.RaiseAndSetIfChanged(ref _calibrationStartTimeStamp, value);
		}

		private double _calibrationDuration = 2.0;
		public double CalibrationDuration
		{
			get => _calibrationDuration;
			set => this.RaiseAndSetIfChanged(ref _calibrationDuration, value);
		}

		private double _calibrationDelay = 2.0;
		public double CalibrationDelay
		{
			get => _calibrationDelay;
			set => this.RaiseAndSetIfChanged(ref _calibrationDelay, value);
		}

		private double _pointDelay = 0.167;
		public double PointDelay
		{
			get => _pointDelay;
			set => this.RaiseAndSetIfChanged(ref _pointDelay, value);
		}

		private int _eyetrackingFPS = 60;
		public int EyetrackingFPS
		{
			get => _eyetrackingFPS;
			set => this.RaiseAndSetIfChanged(ref _eyetrackingFPS, value);
		}

		private double _DPIUnscaleFactor = 1.0;
		public double DPIUnscaleFactor
		{
			get => _DPIUnscaleFactor;
			set => this.RaiseAndSetIfChanged(ref _DPIUnscaleFactor, value);
		}

		private ObservableCollection<Point> _calibrationPoints = new ObservableCollection<Point>();
		public ObservableCollection<Point> CalibrationPoints
		{
			get => _calibrationPoints;
			set => this.RaiseAndSetIfChanged(ref _calibrationPoints, value);
		}

		// objects drawn on the screen to visualize things
		private ObservableCollection<Shape> _shapesToDraw = new ObservableCollection<Shape>();

		public ObservableCollection<Shape> ShapesToDraw
		{
			get => _shapesToDraw;
			set => this.RaiseAndSetIfChanged(ref _shapesToDraw, value);
		}

		private PupilInfo _pupilInfo = null;
		public PupilInfo PupilInfo
		{
			get => _pupilInfo;
			set => this.RaiseAndSetIfChanged(ref _pupilInfo, value);
		}

		private bool _isCalibrationParametersExpanded = SharpEyes.Models.Settings.Current.CalibrationParametersExpanded;
		public bool IsCalibrationParametersExpanded
		{
			get => _isCalibrationParametersExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isCalibrationParametersExpanded, value);
				SharpEyes.Models.Settings.Current.CalibrationParametersExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		}

		private bool _isCalibrationPointsExpanded = SharpEyes.Models.Settings.Current.CalibrationPointsExpanded;
		public bool IsCalibrationPointsExpanded
		{
			get => _isCalibrationPointsExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isCalibrationPointsExpanded, value);
				SharpEyes.Models.Settings.Current.CalibrationPointsExpanded = value;
				SharpEyes.Models.Settings.Current.Save();
			}
		}

		public CalibrationViewModel()
		{
			LoadCalibrationPupilsCommand = ReactiveCommand.Create(LoadCalibrationPupils);
			ImportCalibrationPupilsCommand = ReactiveCommand.Create(ImportCalibrationPupils);
			LoadPupilsToConvertCommand = ReactiveCommand.Create(LoadPupilsToConvert);
			ComputeMappingCommand = ReactiveCommand.Create(ComputeMapping);
			ConvertPupilFinderCommand = ReactiveCommand.Create(ConvertPupilFinderData);
			ForceRedrawCommand = ReactiveCommand.Create(ForceRedraw);

			CalibrationParameters defaults = CalibrationParameters.GetDefault35PointCalibrationParameters();
			foreach (CalibrationIndex index in defaults.calibrationSequence)
				CalibrationPoints.Add(defaults.calibrationPoints[index.Index]);

			ShapesToDraw.Add(new Rectangle()
			{
				Width = StimulusWidth, Height = StimulusHeight,
				StrokeThickness = 4, Stroke = new SolidColorBrush(Colors.DodgerBlue)
			});
		}

		public async void LoadCalibrationPupils()
		{
			OpenFileDialog openFileDialog = new OpenFileDialog()
			{
				Title = "Load Pupils...",
				Filters = { new FileDialogFilter() { Name = "Numpy File (*.npy)", Extensions = { "npy" } } }
			};
			string[] fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;
			string pupilsFile = fileName[0];

			openFileDialog.Title = "Load Timestamps...";
			fileName = await openFileDialog.ShowAsync(MainWindow);

			if (fileName == null || fileName.Length == 0)
				return;
			string timestampsFile = fileName[0];

			PupilInfo = new PupilInfo(Num.load(pupilsFile), Num.load(timestampsFile));
		}

		public void ImportCalibrationPupils()
		{

		}

		public void LoadPupilsToConvert()
		{

		}

		public void ComputeMapping()
		{

		}

		public void ConvertPupilFinderData()
		{

		}

		public void ForceRedraw()
		{
			ShapesToDraw.Clear();
			ShapesToDraw.Add(new Rectangle()
			{
				Width = StimulusWidth,
				Height = StimulusHeight,
				StrokeThickness = 4,
				Stroke = new SolidColorBrush(Colors.DodgerBlue)
			});
			foreach (Point p in CalibrationPoints)
			{
				
			}
		}
	}
}